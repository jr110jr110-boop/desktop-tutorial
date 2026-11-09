using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production.Dnc;
using ViewModels.NasbNiro.ProductionDtos;

namespace MsTests
{
    /// <summary>
    /// Production release P2 part A (DNC): the program check against the real machines of the company (three Heidenhain TNC 4xx and a lathe), the program name on the control, the line endings, the difference of
    /// two revisions, the start gate, the three links against fake devices on the loopback (serial-to-Ethernet converter, FTP server of a control, shared folder) and the whole flow on the in-memory database:
    /// upload, two-person approval, send, delivery, the operator's confirmation, the gate at the start, the record of the run and the program fetched back from the machine.
    /// Every expected figure is worked out by hand in the test.
    /// </summary>
    [TestClass]
    public class DncTests
    {
        // the Mikron VCP 710 of the company: TNC 426/430, three axes, Step-Tec spindle 12000 rpm; tool capacity ★ 30 for the test
        private static readonly NcProfile Mikron = new NcProfile
        {
            Family = MfNcControllerFamily.HeidenhainConversational, Axes = "XYZ", MaxSpindleRpm = 12000, ToolCapacity = 30, MaxFeedMmPerMin = 10000, Name = "Mikron VCP 710",
        };

        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        private const string GoodTnc =
            "0 BEGIN PGM 100 MM\r\n" +
            "1 BLK FORM 0.1 Z X+0 Y+0 Z-40\r\n" +
            "2 BLK FORM 0.2 X+100 Y+100 Z+0\r\n" +
            "3 TOOL CALL 3 Z S8000 F600\r\n" +
            "4 L Z+100 R0 FMAX M3\r\n" +
            "5 L X+10 Y+10 R0 FMAX\r\n" +
            "6 L Z-5 F300\r\n" +
            "7 CC X+50 Y+50\r\n" +
            "8 C X+10 Y+10 DR+\r\n" +
            "9 L Z+100 FMAX M30\r\n" +
            "10 END PGM 100 MM\r\n";

        // ------------------------------------------------------------------------------------------ the program check

        [TestMethod]
        public void A_Heidenhain_Program_For_The_Mikron_Passes_And_Its_Program_Number_Is_Read_From_Begin_Pgm()
        {
            NcAnalysis a = MfNcRules.Analyse(Ascii(GoodTnc), Mikron);
            Assert.AreEqual(0, a.ErrorCount, string.Join("\n", a.Findings.Select(f => f.Stored)));
            Assert.AreEqual(0, a.WarningCount, string.Join("\n", a.Findings.Select(f => f.Stored)));
            Assert.AreEqual("100", a.ProgramNumber);
            Assert.AreEqual(11, a.LineCount);
            Assert.AreEqual(GoodTnc.Length, a.SizeBytes);
            Assert.AreEqual(MfNcRules.Sha256Hex(Ascii(GoodTnc)), a.Sha256);
            Assert.AreEqual(64, a.Sha256.Length);
        }

        [TestMethod]
        public void A_Program_Of_The_Five_Axis_Reiden_Is_Refused_On_The_Three_Axis_Mikron_With_Every_Reason()
        {
            // B axis (Reiden has a rotary table, the Mikron does not), 15000 rpm (Mikron 12000), tool 45 (capacity 30), END PGM of another name and a Persian comment
            string program =
                "0 BEGIN PGM 200 MM\r\n" +
                "1 TOOL CALL 45 Z S15000\r\n" +
                "2 L X+0 Y+0 B+90 R0 FMAX\r\n" +
                "3 L Z-2 F200 ; سوراخ\r\n" +
                "4 END PGM 201 MM\r\n";
            NcAnalysis a = MfNcRules.Analyse(Encoding.UTF8.GetBytes(program), Mikron);
            string[] codes = a.Findings.Where(f => f.Severity == NcSeverity.Error).Select(f => f.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[] { "ASCII", "AXIS", "PGMNAME", "RPM", "TOOL" }, codes, string.Join("\n", a.Findings.Select(f => f.Stored)));
            Assert.AreEqual(3, a.Findings.Single(f => f.Code == "AXIS").Line);
            Assert.AreEqual(2, a.Findings.Single(f => f.Code == "RPM").Line);
            Assert.AreEqual(4, a.Findings.Single(f => f.Code == "ASCII").Line);
            StringAssert.Contains(a.Findings.Single(f => f.Code == "AXIS").Text, "محور B");
        }

        [TestMethod]
        public void The_Reiden_With_Axes_X_Y_Z_B_W_Takes_The_Same_B_Axis_Program()
        {
            var reiden = new NcProfile { Family = MfNcControllerFamily.HeidenhainConversational, Axes = "XYZBW", Name = "Reiden" };
            string program = "0 BEGIN PGM 7 MM\r\n1 L X+0 Y+0 B+90 R0 FMAX\r\n2 L W+10 FMAX\r\n3 END PGM 7 MM\r\n";
            NcAnalysis a = MfNcRules.Analyse(Ascii(program), reiden);
            Assert.AreEqual(0, a.ErrorCount, string.Join("\n", a.Findings.Select(f => f.Stored)));
        }

        [TestMethod]
        public void Broken_Block_Numbers_Of_A_Heidenhain_Program_Give_One_Warning_At_The_First_Gap()
        {
            string program = "0 BEGIN PGM 5 MM\r\n1 L X+0 FMAX\r\n3 L X+5 FMAX\r\n4 L X+6 FMAX\r\n5 END PGM 5 MM\r\n";
            NcAnalysis a = MfNcRules.Analyse(Ascii(program), Mikron);
            Assert.AreEqual(0, a.ErrorCount);
            Assert.AreEqual(1, a.WarningCount);
            NcFinding w = a.Findings.Single();
            Assert.AreEqual("BLOCKNO", w.Code);
            Assert.AreEqual(3, w.Line, "the line with block 3 where 2 was expected");
        }

        private static readonly NcProfile Lathe = new NcProfile
        {
            Family = MfNcControllerFamily.Fanuc, RequirePercent = true, Axes = "XZ", IsLathe = true, MaxSpindleRpm = 2500, ToolCapacity = 8, MaxFeedMmPerMin = 4000, Name = "CMZ TBI520",
        };

        [TestMethod]
        public void A_Lathe_Program_Reads_S_Under_G96_As_Surface_Speed_And_Checks_The_G50_Clamp_And_The_Tool_Of_T_Words()
        {
            string program =
                "%\r\n" +
                "O0012 (FLANGE)\r\n" +
                "G50 S3000\r\n" +        // clamp above the 2500 of the machine: error
                "G96 S200 M3\r\n" +       // 200 m/min, not rpm: no error
                "T0101\r\n" +             // tool 1: fine
                "G0 X50. Z2.\r\n" +
                "G1 Z-20. F0.25\r\n" +    // G99 per revolution (lathe default): no feed check
                "G98 G1 X60. F5000\r\n" + // per minute, above 4000: warning
                "T1212\r\n" +             // tool 12 of a turret of 8: error
                "G0 Y10.\r\n" +           // no Y axis on the lathe: error
                "G71 U1. R0.5\r\n" +      // a cycle: its U is a depth, never an axis
                "M30\r\n" +
                "%\r\n";
            NcAnalysis a = MfNcRules.Analyse(Ascii(program), Lathe);
            Assert.AreEqual("O0012", a.ProgramNumber);
            string[] errors = a.Findings.Where(f => f.Severity == NcSeverity.Error).Select(f => f.Code + "@" + f.Line).ToArray();
            CollectionAssert.AreEqual(new[] { "RPM@3", "TOOL@9", "AXIS@10" }, errors, string.Join("\n", a.Findings.Select(f => f.Stored)));
            string[] warnings = a.Findings.Where(f => f.Severity == NcSeverity.Warning).Select(f => f.Code + "@" + f.Line).ToArray();
            CollectionAssert.AreEqual(new[] { "FEED@8" }, warnings);
        }

        [TestMethod]
        public void A_Tape_Program_Without_Its_Closing_Percent_Or_Its_End_Is_Refused_And_A_Subprogram_Only_Warns()
        {
            NcAnalysis noEnd = MfNcRules.Analyse(Ascii("%\nO0001\nG0 X0 Z0\nM30\n"), Lathe);
            CollectionAssert.AreEqual(new[] { "PERCENT" }, noEnd.Findings.Where(f => f.Severity == NcSeverity.Error).Select(f => f.Code).ToArray());

            NcAnalysis noM30 = MfNcRules.Analyse(Ascii("%\nO0001\nG0 X0 Z0\n%\n"), Lathe);
            CollectionAssert.AreEqual(new[] { "NOEND" }, noM30.Findings.Where(f => f.Severity == NcSeverity.Error).Select(f => f.Code).ToArray());

            NcAnalysis sub = MfNcRules.Analyse(Ascii("%\nO9001\nG0 X0\nM99\n%\n"), Lathe);
            Assert.AreEqual(0, sub.ErrorCount);
            Assert.AreEqual("SUBPROGRAM", sub.Findings.Single().Code);
        }

        [TestMethod]
        public void A_Fanuc_Program_Number_Above_9999_And_An_Empty_File_Are_Refused()
        {
            NcAnalysis big = MfNcRules.Analyse(Ascii("%\nO12345\nG0 X0\nM30\n%\n"), Lathe);
            Assert.AreEqual("PGMNO", big.Findings.Single(f => f.Severity == NcSeverity.Error).Code);
            NcAnalysis empty = MfNcRules.Analyse(Array.Empty<byte>(), Lathe);
            Assert.AreEqual("EMPTY", empty.Findings.Single().Code);
        }

        [TestMethod]
        public void A_Program_Above_The_Memory_Of_The_Control_Is_Refused()
        {
            var small = new NcProfile { Family = MfNcControllerFamily.HeidenhainConversational, MaxBytes = 100, Name = "Deckel Maho" };
            NcAnalysis a = MfNcRules.Analyse(Ascii(GoodTnc), small);
            Assert.AreEqual(GoodTnc.Length > 100, true);
            Assert.AreEqual("SIZE", a.Findings.Single(f => f.Severity == NcSeverity.Error).Code);
        }

        [TestMethod]
        public void The_Stored_Findings_Keep_Severity_Line_Code_And_Text()
        {
            NcAnalysis a = MfNcRules.Analyse(Ascii("%\nO0001\nG0 Y5\nM30\n%\n"), Lathe);
            Assert.AreEqual("E|3|AXIS|" + a.Findings.Single().Text, a.Stored());
        }

        // ------------------------------------------------------------------------------------------ the name on the control, the line endings

        [TestMethod]
        public void The_Program_Name_On_The_Control_Follows_The_Format_And_The_Extension_Of_The_Machine()
        {
            Assert.AreEqual("100.H", MfNcRules.TargetName("{number}", ".H", "100", "P-1", "x.nc").Name);
            Assert.AreEqual("O0012", MfNcRules.TargetName("O{number}", null, "O0012", "P-1", "x.nc").Name);
            Assert.AreEqual("O0012.NC", MfNcRules.TargetName("{O}", "NC", "O0012", "P-1", "x.nc").Name);
            Assert.AreEqual("P-1.H", MfNcRules.TargetName("{code}", ".H", null, "P-1", "x.nc").Name);
            Assert.AreEqual("flange.H", MfNcRules.TargetName(null, ".H", "100", "P-1", "flange.nc").Name, "the machine's extension replaces the file's");
            Assert.AreEqual("100.h", MfNcRules.TargetName(null, ".H", "100", "P-1", "100.h").Name, "an extension already in place stays");
            Assert.IsNotNull(MfNcRules.TargetName("{number}", ".H", null, "P-1", "x.nc").Error, "no number in the file");
            Assert.IsNotNull(MfNcRules.TargetName(null, null, null, "P-1", "فلنج.nc").Error, "a Persian name never reaches the control");
            Assert.IsNotNull(MfNcRules.TargetName("{code}-a-very-long-name-for-an-old-control", null, null, "P-1", "x").Error, "more than 32 characters");
        }

        [TestMethod]
        public void The_Outgoing_Program_Gets_The_Line_Ends_Of_The_Machine_And_One_Final_Line_End()
        {
            byte[] mixed = Ascii("A\nB\r\nC\rD\n\n\n");
            CollectionAssert.AreEqual(Ascii("A\r\nB\r\nC\r\nD\r\n"), MfNcRules.Normalise(mixed, MfNcLineEnding.CrLf));
            CollectionAssert.AreEqual(Ascii("A\nB\nC\nD\n"), MfNcRules.Normalise(mixed, MfNcLineEnding.Lf));
            CollectionAssert.AreEqual(Ascii("A\rB\rC\rD\r"), MfNcRules.Normalise(mixed, MfNcLineEnding.Cr));
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "", "" }, MfNcRules.Lines(mixed));
        }

        // ------------------------------------------------------------------------------------------ the difference of two revisions

        [TestMethod]
        public void The_Difference_Counts_One_Changed_And_One_Added_Line_And_Lists_Them_With_Context()
        {
            var oldLines = new[] { "0 BEGIN PGM 1 MM", "1 L X+0", "2 L X+5 F100", "3 L X+9", "4 END PGM 1 MM" };
            var newLines = new[] { "0 BEGIN PGM 1 MM", "1 L X+0", "2 L X+5 F80", "3 L X+9", "4 L X+12", "5 END PGM 1 MM" };
            NcDiff d = MfNcRules.Diff(oldLines, newLines);
            Assert.AreEqual(2, d.Removed, "the F100 line and the old END line");
            Assert.AreEqual(3, d.Added, "the F80 line, the new X+12 line and the renumbered END line");
            Assert.IsFalse(d.Truncated);
            CollectionAssert.AreEqual(new[]
            {
                "  1 0 BEGIN PGM 1 MM", "  2 1 L X+0", "- 3 2 L X+5 F100", "+ 3 2 L X+5 F80", "  4 3 L X+9", "- 5 4 END PGM 1 MM", "+ 5 4 L X+12", "+ 6 5 END PGM 1 MM",
            }, d.Lines, string.Join("\n", d.Lines));
        }

        [TestMethod]
        public void Equal_Revisions_Have_No_Difference_And_A_New_File_Is_All_Added()
        {
            NcDiff same = MfNcRules.Diff(new[] { "a", "b" }, new[] { "a", "b" });
            Assert.AreEqual(0, same.Added);
            Assert.AreEqual(0, same.Removed);
            Assert.AreEqual(0, same.Lines.Count);
            NcDiff fresh = MfNcRules.Diff(Array.Empty<string>(), new[] { "a", "b", "c" });
            Assert.AreEqual(3, fresh.Added);
            Assert.AreEqual(0, fresh.Removed);
        }

        [TestMethod]
        public void A_Change_Too_Large_To_List_Still_Has_Exact_Counts()
        {
            // 3000 lines, every other one changed: 1500 removed + 1500 added = distance 3000, above the 2000 that are listed
            var oldLines = Enumerable.Range(0, 3000).Select(i => "L" + i).ToList();
            var newLines = Enumerable.Range(0, 3000).Select(i => i % 2 == 0 ? "N" + i : "L" + i).ToList();
            NcDiff d = MfNcRules.Diff(oldLines, newLines);
            Assert.IsTrue(d.Truncated);
            Assert.IsFalse(d.Approximate);
            Assert.AreEqual(1500, d.Removed);
            Assert.AreEqual(1500, d.Added);
            Assert.AreEqual(0, d.Lines.Count);
        }

        [TestMethod]
        public void Two_Completely_Different_Large_Files_Stay_Within_The_Work_Budget_With_Approximate_Counts()
        {
            var oldLines = Enumerable.Range(0, 20000).Select(i => "A" + i).ToList();
            var newLines = Enumerable.Range(0, 20000).Select(i => "B" + i).ToList();
            var clock = Stopwatch.StartNew();
            NcDiff d = MfNcRules.Diff(oldLines, newLines);
            Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(20), "budget: " + clock.Elapsed);
            Assert.IsTrue(d.Approximate);
            Assert.AreEqual(20000, d.Removed);
            Assert.AreEqual(20000, d.Added);
        }

        // ------------------------------------------------------------------------------------------ the start gate

        [TestMethod]
        public void The_Start_Gate_Needs_The_Current_Revision_Confirmed_On_This_Machine_After_Its_Approval()
        {
            var approved = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(3.5));
            MfNcRules.GateTransfer T(int id, int machine, int revision, DateTimeOffset? at, MfNcTransferStatus status = MfNcTransferStatus.Confirmed) =>
                new MfNcRules.GateTransfer { Id = id, MachineId = machine, RevisionId = revision, Status = status, ConfirmedAt = at };

            Assert.IsNull(MfNcRules.StartGate(false, MfBookingKind.Run, "P", 5, approved, 1, null).Error, "the machine does not require it");
            Assert.IsNull(MfNcRules.StartGate(true, MfBookingKind.Waiting, "P", 5, approved, 1, null).Error, "a waiting booking is not machining");
            Assert.IsNull(MfNcRules.StartGate(true, MfBookingKind.Run, "P", null, null, 1, null).Error, "no approved program: nothing to check");
            StringAssert.Contains(MfNcRules.StartGate(true, MfBookingKind.Setup, "P-100", 5, approved, 1, null).Error, "P-100");
            Assert.IsNotNull(MfNcRules.StartGate(true, MfBookingKind.Run, "P", 5, approved, 1, new[] { T(1, 1, 5, approved.AddMinutes(-1)) }).Error, "confirmed before the approval");
            Assert.IsNotNull(MfNcRules.StartGate(true, MfBookingKind.Run, "P", 5, approved, 1, new[] { T(1, 2, 5, approved.AddMinutes(5)) }).Error, "confirmed on another machine");
            Assert.IsNotNull(MfNcRules.StartGate(true, MfBookingKind.Run, "P", 5, approved, 1, new[] { T(1, 1, 4, approved.AddMinutes(5)) }).Error, "an older revision");
            Assert.IsNotNull(MfNcRules.StartGate(true, MfBookingKind.Run, "P", 5, approved, 1, new[] { T(1, 1, 5, null, MfNcTransferStatus.Delivered) }).Error, "delivered but not confirmed");
            var ok = MfNcRules.StartGate(true, MfBookingKind.Run, "P", 5, approved, 1, new[] { T(1, 1, 5, approved.AddMinutes(5)), T(2, 1, 5, approved.AddMinutes(9)) });
            Assert.IsNull(ok.Error);
            Assert.AreEqual(2, ok.Transfer.Id, "the latest confirmation covers the start");
        }

        // ------------------------------------------------------------------------------------------ the links against fake devices

        /// <summary>A serial-to-Ethernet converter on the loopback: it records what is sent to it, or plays back a program as if the operator pressed "output" on the control.</summary>
        private sealed class FakeConverter : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
            public ConcurrentQueue<byte[]> Received { get; } = new ConcurrentQueue<byte[]>();

            /// <summary>What the next connection plays back (null: record).</summary>
            public byte[] PlayBack { get; set; }

            /// <summary>Send XOFF for this long at the start of a recorded transfer.</summary>
            public int XoffMs { get; set; }

            private readonly Task _loop;
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();

            public FakeConverter()
            {
                _listener.Start();
                _loop = Task.Run(LoopAsync);
            }

            private async Task LoopAsync()
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch (OperationCanceledException) { return; }
                    catch (ObjectDisposedException) { return; }
                    using (client)
                    {
                        NetworkStream s = client.GetStream();
                        if (PlayBack != null)
                        {
                            byte[] play = PlayBack;
                            PlayBack = null;
                            for (int i = 0; i < play.Length; i += 50) { await s.WriteAsync(play.AsMemory(i, Math.Min(50, play.Length - i))); await Task.Delay(5); }
                            await Task.Delay(300);
                            continue;
                        }
                        if (XoffMs > 0)
                        {
                            await s.WriteAsync(new byte[] { 0x13 });
                            _ = Task.Run(async () => { await Task.Delay(XoffMs); try { await s.WriteAsync(new byte[] { 0x11 }); } catch (Exception) { } });
                        }
                        using var ms = new MemoryStream();
                        byte[] buffer = new byte[4096];
                        int read;
                        try { while ((read = await s.ReadAsync(buffer)) > 0) ms.Write(buffer, 0, read); }
                        catch (IOException) { }
                        Received.Enqueue(ms.ToArray());
                    }
                }
            }

            public byte[] WaitReceived()
            {
                for (int i = 0; i < 200 && Received.IsEmpty; i++) Thread.Sleep(25);
                Assert.IsTrue(Received.TryDequeue(out byte[] bytes), "the converter received nothing");
                return bytes;
            }

            public void Dispose()
            {
                _stop.Cancel();
                _listener.Stop();
                try { _loop.Wait(2000); } catch (AggregateException) { }
            }
        }

        [TestMethod]
        public async Task The_Serial_Link_Waits_Out_An_Xoff_And_Paces_The_Program_To_The_Baud_Rate()
        {
            using var converter = new FakeConverter { XoffMs = 400 };
            byte[] program = Ascii(string.Concat(Enumerable.Repeat("1 L X+10 Y+10 R0 FMAX\r\n", 50)));   // 1150 bytes
            var clock = Stopwatch.StartNew();
            NcLinkResult r = await new SerialGatewayLink().SendAsync(new NcLinkTarget { Host = "127.0.0.1", Port = converter.Port, BaudRate = 38400 }, "100.H", program, CancellationToken.None);
            clock.Stop();
            Assert.IsTrue(r.Ok, r.Message);
            CollectionAssert.AreEqual(program, converter.WaitReceived());
            // 1150 characters x 10 bits / 38400 baud = 299 ms on the line, plus the 400 ms the control held the line with XOFF
            Assert.IsTrue(clock.ElapsedMilliseconds >= 600, "paced and paused: " + clock.ElapsedMilliseconds + " ms");
        }

        [TestMethod]
        public async Task The_Serial_Link_Receives_A_Heidenhain_Program_Up_To_End_Pgm_And_A_Tape_Program_Up_To_The_Closing_Percent()
        {
            using var converter = new FakeConverter { PlayBack = Ascii(GoodTnc) };
            var target = new NcLinkTarget { Host = "127.0.0.1", Port = converter.Port, Family = MfNcControllerFamily.HeidenhainConversational, ReceiveWaitSeconds = 5, ReceiveIdleSeconds = 5 };
            var clock = Stopwatch.StartNew();
            NcLinkResult r = await new SerialGatewayLink().ReceiveAsync(target, "100.H", CancellationToken.None);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(GoodTnc, Encoding.ASCII.GetString(r.Bytes));
            Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(4), "it stops at END PGM, not at the idle time");

            Assert.IsTrue(SerialGatewayLink.Complete(Ascii("%\nO1\nM30\n%").ToList(), MfNcControllerFamily.Fanuc));
            Assert.IsFalse(SerialGatewayLink.Complete(Ascii("%\nO1\nM30\n").ToList(), MfNcControllerFamily.Fanuc));
            Assert.IsFalse(SerialGatewayLink.Complete(Ascii("0 BEGIN PGM 1 MM\r\n1 L X+0").ToList(), MfNcControllerFamily.HeidenhainConversational));
        }

        [TestMethod]
        public async Task The_Serial_Link_Says_Plainly_When_Nobody_Answers()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            NcLinkResult r = await new SerialGatewayLink().SendAsync(new NcLinkTarget { Host = "127.0.0.1", Port = port, TimeoutSeconds = 3 }, "x", Ascii("x"), CancellationToken.None);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains(r.Message, "اتصال برقرار نشد");
        }

        /// <summary>A minimal FTP server of a control: one user, passive mode, files in memory.</summary>
        private sealed class FakeFtp : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            public ConcurrentDictionary<string, byte[]> Files { get; } = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
            public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
            public string Cwd { get; private set; } = "/";

            public FakeFtp()
            {
                _listener.Start();
                _ = Task.Run(LoopAsync);
            }

            private async Task LoopAsync()
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch (Exception) { return; }
                    _ = Task.Run(() => SessionAsync(client));
                }
            }

            private async Task SessionAsync(TcpClient client)
            {
                using (client)
                {
                    NetworkStream s = client.GetStream();
                    var reader = new StreamReader(s, Encoding.ASCII);
                    async Task Say(string line) => await s.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"));
                    await Say("220-Fake control\r\n220 ready");
                    TcpListener data = null;
                    string user = null;
                    while (true)
                    {
                        string line = await reader.ReadLineAsync();
                        if (line == null) return;
                        string cmd = line.Split(' ')[0].ToUpperInvariant(), arg = line.Length > cmd.Length ? line.Substring(cmd.Length + 1) : "";
                        switch (cmd)
                        {
                            case "USER": user = arg; await Say("331 password"); break;
                            case "PASS": await Say(user == "cnc" && arg == "secret" ? "230 ok" : "530 denied"); break;
                            case "TYPE": await Say("200 binary"); break;
                            case "CWD": Cwd = arg; await Say(arg == "/nc" ? "250 ok" : "550 no"); break;
                            case "PASV":
                                data = new TcpListener(IPAddress.Loopback, 0);
                                data.Start();
                                int p = ((IPEndPoint)data.LocalEndpoint).Port;
                                await Say($"227 Entering Passive Mode (10,0,0,9,{p / 256},{p % 256})");   // an inner address: the client must use the same host instead
                                break;
                            case "STOR":
                            {
                                await Say("150 send");
                                using TcpClient d = await data.AcceptTcpClientAsync();
                                using var ms = new MemoryStream();
                                await d.GetStream().CopyToAsync(ms);
                                Files[arg] = ms.ToArray();
                                data.Stop();
                                await Say("226 stored");
                                break;
                            }
                            case "RETR":
                            {
                                if (!Files.TryGetValue(arg, out byte[] f)) { await Say("550 missing"); data.Stop(); break; }
                                await Say("150 here");
                                using (TcpClient d = await data.AcceptTcpClientAsync()) await d.GetStream().WriteAsync(f);
                                data.Stop();
                                await Say("226 done");
                                break;
                            }
                            case "QUIT": await Say("221 bye"); return;
                            default: await Say("502 no"); break;
                        }
                    }
                }
            }

            public void Dispose()
            {
                _stop.Cancel();
                _listener.Stop();
            }
        }

        [TestMethod]
        public async Task The_Ftp_Link_Stores_And_Fetches_A_Program_In_The_Folder_Of_The_Control_And_Refuses_A_Wrong_Password()
        {
            using var ftp = new FakeFtp();
            var target = new NcLinkTarget { Host = "127.0.0.1", Port = ftp.Port, User = "cnc", Password = "secret", RemotePath = "/nc", TimeoutSeconds = 5 };
            var link = new FtpLink();
            Assert.IsTrue((await link.TestAsync(target, CancellationToken.None)).Ok);
            byte[] program = Ascii(GoodTnc);
            NcLinkResult sent = await link.SendAsync(target, "100.H", program, CancellationToken.None);
            Assert.IsTrue(sent.Ok, sent.Message);
            CollectionAssert.AreEqual(program, ftp.Files["100.H"]);
            NcLinkResult got = await link.ReceiveAsync(target, "100.H", CancellationToken.None);
            Assert.IsTrue(got.Ok, got.Message);
            CollectionAssert.AreEqual(program, got.Bytes);
            NcLinkResult missing = await link.ReceiveAsync(target, "999.H", CancellationToken.None);
            Assert.IsFalse(missing.Ok);

            NcLinkResult denied = await link.SendAsync(new NcLinkTarget { Host = "127.0.0.1", Port = ftp.Port, User = "cnc", Password = "wrong", TimeoutSeconds = 5 }, "x.H", program, CancellationToken.None);
            Assert.IsFalse(denied.Ok);
            StringAssert.Contains(denied.Message, "ورود به سرور FTP پذیرفته نشد");
        }

        [TestMethod]
        public async Task The_Folder_Link_Writes_Through_A_Temporary_Name_And_Reads_The_File_Back()
        {
            string folder = Path.Combine(Path.GetTempPath(), "dnc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var link = new NetworkFolderLink();
                var target = new NcLinkTarget { RemotePath = folder };
                byte[] program = Ascii(GoodTnc);
                Assert.IsTrue((await link.SendAsync(target, "100.H", program, CancellationToken.None)).Ok);
                CollectionAssert.AreEqual(new[] { "100.H" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray(), "no temporary file is left");
                CollectionAssert.AreEqual(program, (await link.ReceiveAsync(target, "100.H", CancellationToken.None)).Bytes);
                Assert.IsFalse((await link.SendAsync(new NcLinkTarget { RemotePath = Path.Combine(folder, "missing") }, "100.H", program, CancellationToken.None)).Ok);
            }
            finally { Directory.Delete(folder, true); }
        }

        // ------------------------------------------------------------------------------------------ review fixes (independent review of the DNC backend)

        [TestMethod]
        public void Lan_Addresses_Folder_Paths_And_Program_Names_Are_Checked()
        {
            Assert.IsTrue(NcLan.IsLanHost("192.168.10.21", false));
            Assert.IsTrue(NcLan.IsLanHost("10.0.0.5", false));
            Assert.IsTrue(NcLan.IsLanHost("172.20.1.1", false));
            Assert.IsFalse(NcLan.IsLanHost("8.8.8.8", false), "an outside host never gets the FTP password");
            Assert.IsFalse(NcLan.IsLanHost("172.32.0.1", false));
            Assert.IsFalse(NcLan.IsLanHost("cnc.example.com", false), "addresses only, no names");
            Assert.IsFalse(NcLan.IsLanHost("10.1", false), "the short form is refused");
            Assert.IsFalse(NcLan.IsLanHost("127.0.0.1", false), "the server itself only when the settings allow it");
            Assert.IsTrue(NcLan.IsLanHost("127.0.0.1", true));
            Assert.IsTrue(NcLan.HasControl("nc\r\nDELE O1234"));

            string root = Path.Combine(Path.GetTempPath(), "dnc-r");
            Assert.IsTrue(NcLan.UnderRoot(Path.Combine(root, "mikron"), new[] { root }));
            Assert.IsFalse(NcLan.UnderRoot(root + "-other", new[] { root }), "a sibling folder with the same prefix is not inside");
            Assert.IsFalse(NcLan.UnderRoot(Path.Combine(root, "..", "etc"), new[] { root }));
            Assert.IsTrue(NcLan.TouchesApplication(AppContext.BaseDirectory));
            Assert.IsNull(NcLan.FileIn(root, "../x.H"));
            Assert.IsNull(NcLan.FileIn(root, ".."));
            Assert.IsNotNull(NcLan.FileIn(root, "100.H"));

            Assert.IsTrue(MfNcRules.IsNcFileName("100.H"));
            Assert.IsTrue(MfNcRules.IsNcFileName("O1234"));
            Assert.IsTrue(MfNcRules.IsNcFileName("PART_1.MPF"));
            Assert.IsFalse(MfNcRules.IsNcFileName("appsettings.json"));
            Assert.IsFalse(MfNcRules.IsNcFileName("web.config"));
            Assert.IsFalse(MfNcRules.IsNcFileName(".H"));
            Assert.IsNotNull(MfNcRules.TargetName("{code}", ".json", "100", "appsettings", "x.h").Error, "a CNC extension only");
        }

        [TestMethod]
        public void A_Malformed_Number_In_A_Program_Is_Not_A_Crash()
        {
            var profile = new NcProfile { Family = MfNcControllerFamily.Fanuc, ToolCapacity = 12, MaxSpindleRpm = 4000, IsLathe = true, Name = "CMZ" };
            NcAnalysis a = MfNcRules.Analyse(Ascii("%\nO0012\nG123456789012 X0\nT99999999999999\nT12\nM30\n%\n"), profile);
            Assert.IsNotNull(a, "the analysis answers instead of throwing");
            Assert.IsFalse(a.Findings.Any(x => x.Code == "TOOL" && x.Text.Contains(" 12 ")), "a two-digit lathe T is not read as tool 12");
        }

        [TestMethod]
        public void A_Serial_Transfer_Gets_The_Time_Its_Bytes_Need()
        {
            Assert.AreEqual(TimeSpan.FromMinutes(10), MfDncService.AllowedSending(MfNcLinkKind.Ftp, 4_000_000, null));
            Assert.IsTrue(MfDncService.AllowedSending(MfNcLinkKind.SerialGateway, 4_000_000, 9600) > TimeSpan.FromMinutes(80), "4 MB at 9600 baud needs about 70 minutes");
        }

        [TestMethod]
        public async Task The_Folder_Link_Stays_Inside_The_Allowed_Roots_And_Refuses_Big_Files()
        {
            string root = Path.Combine(Path.GetTempPath(), "dnc-root-" + Guid.NewGuid().ToString("N")), machine = Path.Combine(root, "mikron");
            string outside = Path.Combine(Path.GetTempPath(), "dnc-out-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(machine);
            Directory.CreateDirectory(outside);
            try
            {
                var link = new NetworkFolderLink();
                byte[] program = Ascii(GoodTnc);
                Assert.IsTrue((await link.SendAsync(new NcLinkTarget { RemotePath = machine, FolderRoots = new[] { root } }, "100.H", program, CancellationToken.None)).Ok);
                Assert.IsFalse((await link.SendAsync(new NcLinkTarget { RemotePath = outside, FolderRoots = new[] { root } }, "100.H", program, CancellationToken.None)).Ok, "outside the roots");
                Assert.IsFalse((await link.SendAsync(new NcLinkTarget { RemotePath = machine, FolderRoots = Array.Empty<string>() }, "100.H", program, CancellationToken.None)).Ok, "no root allowed");
                Assert.IsFalse((await link.ReceiveAsync(new NcLinkTarget { RemotePath = machine, FolderRoots = new[] { root } }, "../../x.H", CancellationToken.None)).Ok, "the name never leaves the folder");
                Assert.IsFalse((await link.ReceiveAsync(new NcLinkTarget { RemotePath = machine, FolderRoots = new[] { root }, MaxBytes = 10 }, "100.H", CancellationToken.None)).Ok, "bigger than the control's memory");
                Assert.IsFalse(File.Exists(Path.Combine(outside, "100.H")));
            }
            finally
            {
                Directory.Delete(root, true);
                Directory.Delete(outside, true);
            }
        }

        [TestMethod]
        public async Task A_Link_Editor_Cannot_Point_A_Machine_At_Server_Files_Or_Outside_Hosts()
        {
            using var f = new Flow();
            f.Db.Set<Machine>().Add(new Machine { Id = 1, Title = "Mikron VCP 710", IsActive = true });
            await f.Db.SaveChangesAsync();
            f.NewRequest(1, "PRODUCTIONMANAGER");
            SaveMachineConnectionDto Link(MfNcLinkKind kind, string host = null, string path = null, string format = "{number}", string ext = ".H") => new SaveMachineConnectionDto
            {
                MachineId = 1, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, LinkKind = kind, Host = host, Port = 21, RemotePath = path, ProgramNameFormat = format, FileExtension = ext,
            };
            StringAssert.Contains((await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.NetworkFolder, path: AppContext.BaseDirectory), default)).Message, "Dnc:FolderRoots", "the application folder");
            StringAssert.Contains((await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.NetworkFolder, path: Path.GetTempPath()), default)).Message, "Dnc:FolderRoots", "a folder outside the roots");
            Assert.IsFalse((await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.NetworkFolder, path: "relative\\nc"), default)).Success, "a relative path");
            Assert.IsFalse((await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.NetworkFolder, path: f.FolderRoot, ext: ".json"), default)).Success, "not a CNC extension");
            StringAssert.Contains((await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.NetworkFolder, path: f.FolderRoot, format: "web", ext: ".H"), default)).Message, "{number}", "a fixed name");
            StringAssert.Contains((await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.Ftp, host: "8.8.8.8"), default)).Message, "شبکه داخلی");
            Assert.IsFalse((await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.Ftp, host: "192.168.10.21", path: "nc\r\nDELE O1"), default)).Success, "CR LF in the FTP folder");
            Ok(await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.NetworkFolder, path: Path.Combine(f.FolderRoot, "mikron")), default), "a machine folder under the allowed root");
            Ok(await f.Dnc.SaveConnectionAsync(Link(MfNcLinkKind.Ftp, host: "192.168.10.21", path: "/nc"), default), "an FTP control on the LAN");
        }

        /// <summary>Program 3 (machine 2, folder link under the allowed root) with approved revisions 30 (current unless told) and 31.</summary>
        private static async Task<string> SeedFolderMachineAsync(Flow f, int currentRevision)
        {
            string folder = Path.Combine(f.FolderRoot, "reiden");
            Directory.CreateDirectory(folder);
            ApplicationDbContext db = f.Db;
            db.Set<Machine>().Add(new Machine { Id = 2, Title = "Reiden", IsActive = true });
            db.Set<Entities.Model.NasbNiro.Inventory.OperationTemplate>().Add(new Entities.Model.NasbNiro.Inventory.OperationTemplate { Id = 5, Title = "فرز کاری" });
            db.Set<MfMachineConnection>().Add(new MfMachineConnection
            {
                Id = 2, MachineId = 2, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, LinkKind = MfNcLinkKind.NetworkFolder, RemotePath = folder, ProgramNameFormat = "{number}",
                FileExtension = ".H", IsActive = true, RequireConfirmedProgram = true,
            });
            db.Set<MfNcProgram>().Add(new MfNcProgram { Id = 3, Code = "R-1", Title = "x", MachineId = 2, OperationTemplateId = 5, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, Status = MfNcProgramStatus.Active, CurrentRevisionId = currentRevision, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
            byte[] p1 = Ascii(GoodTnc), p2 = Ascii(GoodTnc.Replace("F300", "F250"));
            DateTimeOffset approved = DateTimeOffset.Now.AddHours(-2);
            db.Set<MfNcRevision>().Add(new MfNcRevision { Id = 30, ProgramId = 3, Revision = 1, Status = MfNcRevisionStatus.Approved, FileName = "100.h", Sha256 = MfNcRules.Sha256Hex(p1), ProgramNumber = "100", SizeBytes = p1.Length, ApprovedAt = approved, CreateDate = approved });
            db.Set<MfNcRevision>().Add(new MfNcRevision { Id = 31, ProgramId = 3, Revision = 2, Status = MfNcRevisionStatus.Approved, FileName = "100.h", Sha256 = MfNcRules.Sha256Hex(p2), ProgramNumber = "100", SizeBytes = p2.Length, ApprovedAt = approved, CreateDate = approved });
            db.Set<MfNcContent>().Add(new MfNcContent { Id = 30, RevisionId = 30, Bytes = p1 });
            db.Set<MfNcContent>().Add(new MfNcContent { Id = 31, RevisionId = 31, Bytes = p2 });
            db.Set<MfOperation>().Add(new MfOperation { Id = 1, OrderId = 1, Sequence = 10, OperationTemplateId = 5, Title = "فرز کاری", Status = MfOperationStatus.Ready });
            await db.SaveChangesAsync();
            return folder;
        }

        [TestMethod]
        public async Task A_Late_Retry_Of_An_Older_Revision_Never_Overwrites_The_Current_Program()
        {
            using var f = new Flow();
            string folder = await SeedFolderMachineAsync(f, 31);
            DateTimeOffset now = DateTimeOffset.Now;
            // revision 1 failed earlier and waits for its automatic retry; meanwhile revision 2 became current, was delivered and confirmed
            f.Db.Set<MfNcTransfer>().Add(new MfNcTransfer { Id = 1, Direction = MfNcTransferDirection.ToMachine, MachineId = 2, ProgramId = 3, RevisionId = 30, TargetName = "100.H", LinkKind = MfNcLinkKind.NetworkFolder,
                Status = MfNcTransferStatus.Failed, Attempts = 1, NextAttemptAt = now.AddSeconds(-1), RequestedByUserId = 13, RequestedAt = now.AddHours(-1), ModifyDate = now.AddHours(-1) });
            f.Db.Set<MfNcTransfer>().Add(new MfNcTransfer { Id = 2, Direction = MfNcTransferDirection.ToMachine, MachineId = 2, ProgramId = 3, RevisionId = 31, TargetName = "100.H", LinkKind = MfNcLinkKind.NetworkFolder,
                Status = MfNcTransferStatus.Confirmed, Attempts = 1, DeliveredAt = now.AddMinutes(-5), ConfirmedAt = now.AddMinutes(-4), RequestedByUserId = 13, RequestedAt = now.AddMinutes(-6), ModifyDate = now.AddMinutes(-4) });
            await f.Db.SaveChangesAsync();
            File.WriteAllBytes(Path.Combine(folder, "100.H"), Ascii(GoodTnc.Replace("F300", "F250")));

            f.NewRequest(1, "PRODUCTIONMANAGER");
            NcRunPendingResultDto r = await f.Dnc.ProcessPendingAsync(default);
            Assert.AreEqual(0, r.Delivered);
            f.NewRequest(1, "PRODUCTIONMANAGER");
            MfNcTransfer old = await f.Db.Set<MfNcTransfer>().AsNoTracking().FirstAsync(t => t.Id == 1);
            Assert.AreEqual(MfNcTransferStatus.Cancelled, old.Status, "the old revision is cancelled by the system");
            CollectionAssert.AreEqual(Ascii(GoodTnc.Replace("F300", "F250")), File.ReadAllBytes(Path.Combine(folder, "100.H")), "the confirmed revision stays on the control");
            MfOperation op = await f.Db.Set<MfOperation>().AsNoTracking().FirstAsync();
            Assert.IsNull((await MfDncGate.CheckStartAsync(f.Db, op, 2, MfBookingKind.Run, default)).Error);
        }

        [TestMethod]
        public async Task A_Confirmation_Stops_Counting_Once_Another_Program_Was_Written_To_The_Same_File()
        {
            using var f = new Flow();
            await SeedFolderMachineAsync(f, 31);
            DateTimeOffset now = DateTimeOffset.Now;
            f.Db.Set<MfNcTransfer>().Add(new MfNcTransfer { Id = 2, Direction = MfNcTransferDirection.ToMachine, MachineId = 2, ProgramId = 3, RevisionId = 31, TargetName = "100.H", LinkKind = MfNcLinkKind.NetworkFolder,
                Status = MfNcTransferStatus.Confirmed, DeliveredAt = now.AddMinutes(-30), ConfirmedAt = now.AddMinutes(-29), RequestedByUserId = 13, RequestedAt = now.AddMinutes(-31), ModifyDate = now.AddMinutes(-29) });
            // another program with the same number (BEGIN PGM 100) was delivered to the same file afterwards
            f.Db.Set<MfNcTransfer>().Add(new MfNcTransfer { Id = 3, Direction = MfNcTransferDirection.ToMachine, MachineId = 2, ProgramId = 99, RevisionId = 990, TargetName = "100.H", LinkKind = MfNcLinkKind.NetworkFolder,
                Status = MfNcTransferStatus.Delivered, DeliveredAt = now.AddMinutes(-10), RequestedByUserId = 13, RequestedAt = now.AddMinutes(-11), ModifyDate = now.AddMinutes(-10) });
            await f.Db.SaveChangesAsync();
            MfOperation op = await f.Db.Set<MfOperation>().AsNoTracking().FirstAsync();
            var gate = await MfDncGate.CheckStartAsync(f.Db, op, 2, MfBookingKind.Run, default);
            StringAssert.Contains(gate.Error, "نام", "the start is closed: the file on the control was written over");
            Assert.IsNotNull((await MfDncGate.CheckStartAsync(f.Db, op, 2, MfBookingKind.Rework, default)).Error, "rework runs the program too");
            Assert.IsNull((await MfDncGate.CheckStartAsync(f.Db, op, 2, MfBookingKind.Waiting, default)).Error);
        }

        [TestMethod]
        public async Task Every_Program_Of_The_Step_Must_Be_Approved_On_A_Machine_That_Requires_It()
        {
            using var f = new Flow();
            await SeedFolderMachineAsync(f, 31);
            DateTimeOffset now = DateTimeOffset.Now;
            f.Db.Set<MfNcTransfer>().Add(new MfNcTransfer { Id = 2, Direction = MfNcTransferDirection.ToMachine, MachineId = 2, ProgramId = 3, RevisionId = 31, TargetName = "100.H", LinkKind = MfNcLinkKind.NetworkFolder,
                Status = MfNcTransferStatus.Confirmed, DeliveredAt = now.AddMinutes(-5), ConfirmedAt = now.AddMinutes(-4), RequestedByUserId = 13, RequestedAt = now.AddMinutes(-6), ModifyDate = now.AddMinutes(-4) });
            // the second clamping of the same step: a program with drafts only
            f.Db.Set<MfNcProgram>().Add(new MfNcProgram { Id = 4, Code = "R-1B", Title = "گیره دوم", OperationTemplateId = 5, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, Status = MfNcProgramStatus.Active, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
            await f.Db.SaveChangesAsync();
            MfOperation op = await f.Db.Set<MfOperation>().AsNoTracking().FirstAsync();
            StringAssert.Contains((await MfDncGate.CheckStartAsync(f.Db, op, 2, MfBookingKind.Setup, default)).Error, "R-1B");
            Assert.AreEqual(3, (await MfDncGate.ProgramForAsync(f.Db, op, 2, default)).Id, "the terminal shows the approved program first");

            // the program is no longer needed: one writer cannot switch it off or move it; an approver can, with a reason
            f.NewRequest(11, "ENGINEERING");
            NcProgramDto approvedProgram = (await f.Dnc.GetProgramAsync(3, default)).Data;
            var move = new SaveNcProgramDto { Id = 3, Code = "R-1", Title = "x", MachineId = 2, OperationTemplateId = null, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, Status = MfNcProgramStatus.Active, RowVersion = approvedProgram.RowVersion };
            StringAssert.Contains((await f.Dnc.SaveProgramAsync(move, default)).Message, "تغییر نمی‌کند");
            f.NewRequest(1, "PMO");
            var obsolete = new SaveNcProgramDto { Id = 3, Code = "R-1", Title = "x", MachineId = 2, OperationTemplateId = 5, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, Status = MfNcProgramStatus.Obsolete, RowVersion = approvedProgram.RowVersion };
            StringAssert.Contains((await f.Dnc.SaveProgramAsync(obsolete, default)).Message, "تأییدکنندگان");
            f.NewRequest(12, "PRODUCTIONMANAGER");
            StringAssert.Contains((await f.Dnc.SaveProgramAsync(obsolete, default)).Message, "دلیل");
            obsolete.Note = "قطعه از تولید خارج شد";
            Ok(await f.Dnc.SaveProgramAsync(obsolete, default), "obsolete with a reason");
        }

        // ------------------------------------------------------------------------------------------ the model

        [TestMethod]
        public void The_Model_Carries_The_Dnc_Tables_With_Their_Keys_And_Unique_Indexes()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using IServiceScope scope = provider.CreateScope();
            var model = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
            var tables = new Dictionary<Type, string>
            {
                [typeof(MfMachineConnection)] = "MfMachineConnections", [typeof(MfNcProgram)] = "MfNcPrograms", [typeof(MfNcRevision)] = "MfNcRevisions",
                [typeof(MfNcContent)] = "MfNcContents", [typeof(MfNcTransfer)] = "MfNcTransfers", [typeof(MfNcRun)] = "MfNcRuns",
            };
            foreach (var t in tables) Assert.AreEqual(t.Value, model.FindEntityType(t.Key).GetTableName(), t.Key.Name);
            bool Unique(Type type, params string[] columns) => model.FindEntityType(type).GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(columns));
            Assert.IsTrue(Unique(typeof(MfMachineConnection), "MachineId"));
            Assert.IsTrue(Unique(typeof(MfNcProgram), "Code"));
            Assert.IsTrue(Unique(typeof(MfNcRevision), "ProgramId", "Revision"));
            Assert.IsTrue(Unique(typeof(MfNcContent), "RevisionId"));
            bool Fk(Type type, string property) => model.FindEntityType(type).GetForeignKeys().Any(f => f.Properties.Any(p => p.Name == property));
            Assert.IsTrue(Fk(typeof(MfNcTransfer), "RevisionId") && Fk(typeof(MfNcTransfer), "OperationId") && Fk(typeof(MfNcRun), "RevisionId") && Fk(typeof(MfNcRun), "EmployeeId"));
            Assert.AreEqual(true, model.FindEntityType(typeof(MfMachineConnection)).FindProperty("RequireConfirmedProgram").Sentinel);
            Assert.IsNull(model.FindEntityType(typeof(MfMachineConnection)).FindProperty("Password"), "no secret column");
        }

        // ------------------------------------------------------------------------------------------ the whole flow on the in-memory database

        /// <summary>The signed-in user; the test switches it between the programmer, the approver and the operator.</summary>
        private sealed class SwitchUser : IInventoryUserContext
        {
            public int UserId { get; set; }
            public string Roles { get; set; } = "";
            public bool IsInRole(string role) => (role ?? "").Split(',').Select(r => r.Trim()).Any(r => Roles.Split(',').Contains(r));
        }

        private sealed class Flow : IDisposable
        {
            public ServiceProvider Provider { get; }
            public IServiceScope Scope { get; private set; }
            public SwitchUser User { get; } = new SwitchUser();
            public FakeConverter Converter { get; } = new FakeConverter();
            public ApplicationDbContext Db => Scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            public IMfDncService Dnc => Scope.ServiceProvider.GetRequiredService<IMfDncService>();

            /// <summary>The one folder the server settings allow for network-folder links in this test (Dnc:FolderRoots).</summary>
            public string FolderRoot { get; } = Path.Combine(Path.GetTempPath(), "dnc-root-" + Guid.NewGuid().ToString("N"));

            public Flow()
            {
                Directory.CreateDirectory(FolderRoot);
                // P2 part E: the CAM evidence at submit has its own tests (GuardTests); this flow is about the program library and the transfers
                var settings = new Dictionary<string, string> { ["Dnc:AllowLoopback"] = "true", ["Dnc:FolderRoots:0"] = FolderRoot, ["Dnc:RequireCamEvidence"] = "false" };
                Provider = CeoOverviewServiceTests.InMemory(s =>
                {
                    s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
                    s.AddScoped<IInventoryUserContext>(_ => User);
                    s.AddSingleton<IMfNcLinkFactory, MfNcLinkFactory>();
                    s.AddScoped<IMfDncService, MfDncService>();
                });
                Scope = Provider.CreateScope();
            }

            /// <summary>A fresh unit of work, as a new request would have (nothing tracked from the last call).</summary>
            public void NewRequest(int userId, string roles)
            {
                Scope.Dispose();
                Scope = Provider.CreateScope();
                User.UserId = userId;
                User.Roles = roles;
            }

            public void Dispose()
            {
                Scope.Dispose();
                Provider.Dispose();
                Converter.Dispose();
                try { Directory.Delete(FolderRoot, true); } catch (IOException) { }
            }
        }

        private static void Ok<T>(Common.Results.ResponseDto<T> r, string what) => Assert.IsTrue(r.Success, what + ": " + r.Message);

        [TestMethod]
        public async Task The_Whole_Dnc_Flow_From_Upload_To_The_Record_Of_The_Run_And_Back_From_The_Machine()
        {
            using var f = new Flow();
            const int Programmer = 11, Approver = 12, Operator = 13;
            // the shop: the Mikron in work centre 1 behind a serial converter; an operation of routing step 5 waits in that centre
            ApplicationDbContext db = f.Db;
            db.Set<Machine>().Add(new Machine { Id = 1, Title = "Mikron VCP 710", IsActive = true });
            db.Set<MfWorkCenter>().Add(new MfWorkCenter { Id = 1, Code = "CNC-MILL", Title = "فرز CNC", Kind = MfWorkCenterKind.Machine });
            db.Set<Entities.Model.NasbNiro.Inventory.OperationTemplate>().Add(new Entities.Model.NasbNiro.Inventory.OperationTemplate { Id = 5, Title = "فرز کاری" });
            db.Set<MfMachineInfo>().Add(new MfMachineInfo { Id = 1, MachineId = 1, WorkCenterId = 1, Status = MfMachineStatus.Available });
            db.Set<MfProductionOrder>().Add(new MfProductionOrder { Id = 1, Number = "PO-1405-1", WorkOrderId = 1, ContractId = 1, Quantity = 4, Status = MfOrderStatus.Released });
            db.Set<MfOperation>().Add(new MfOperation { Id = 1, OrderId = 1, Sequence = 20, OperationTemplateId = 5, Title = "فرز کاری", WorkCenterId = 1, Status = MfOperationStatus.Ready });
            await db.SaveChangesAsync();

            // 1. production sets the link of the machine (only a master editor may)
            f.NewRequest(Operator, "OPERATOR");
            var link = new SaveMachineConnectionDto
            {
                MachineId = 1, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, ControllerModel = "TNC 426/430", LinkKind = MfNcLinkKind.SerialGateway,
                Host = "127.0.0.1", Port = f.Converter.Port, ProgramNameFormat = "{number}", FileExtension = ".H", Axes = "XYZ", MaxSpindleRpm = 12000, ToolCapacity = 30, BaudRate = 115200,
            };
            Assert.IsFalse((await f.Dnc.SaveConnectionAsync(link, default)).Success, "an operator does not set machine links");
            f.NewRequest(1, "PRODUCTIONMANAGER");
            Ok(await f.Dnc.SaveConnectionAsync(link, default), "save link");

            // 2. the programmer opens the program and uploads revision 1
            f.NewRequest(Programmer, "ENGINEERING");
            var program = await f.Dnc.SaveProgramAsync(new SaveNcProgramDto { Code = "P-100", Title = "فلنج - فرز", OperationTemplateId = 5, WorkCenterId = 1, ControllerFamily = MfNcControllerFamily.HeidenhainConversational }, default);
            Ok(program, "save program");
            int programId = program.Data.Id;
            var r1 = await f.Dnc.UploadRevisionAsync(new UploadNcRevisionDto { ProgramId = programId, FileName = "flange.h", ContentBase64 = Convert.ToBase64String(Ascii(GoodTnc)), ChangeNote = "نسخه اول" }, default);
            Ok(r1, "upload");
            Assert.AreEqual(1, r1.Data.Revision);
            Assert.AreEqual(MfNcRevisionStatus.Draft, r1.Data.Status);
            Assert.AreEqual("100", r1.Data.ProgramNumber);
            Assert.AreEqual(0, r1.Data.ErrorCount);
            CollectionAssert.AreEqual(new[] { "submit", "withdraw" }, r1.Data.AllowedActions);
            Ok(await f.Dnc.RevisionActionAsync(new NcRevisionActionDto { RevisionId = r1.Data.Id, Action = "submit" }, default), "submit");

            // 3. the author never approves; another person does
            f.NewRequest(Programmer, "ENGINEERING");
            var self = await f.Dnc.RevisionActionAsync(new NcRevisionActionDto { RevisionId = r1.Data.Id, Action = "approve" }, default);
            Assert.IsFalse(self.Success);
            StringAssert.Contains(self.Message, "شخص دیگری");
            f.NewRequest(Approver, "PRODUCTIONMANAGER");
            var approved = await f.Dnc.RevisionActionAsync(new NcRevisionActionDto { RevisionId = r1.Data.Id, Action = "approve" }, default);
            Ok(approved, "approve");
            Assert.AreEqual(MfNcRevisionStatus.Approved, approved.Data.Status);
            Assert.AreEqual(r1.Data.Id, (await f.Dnc.GetProgramAsync(programId, default)).Data.CurrentRevisionId);

            // 4. the operation may not start on the Mikron before the program is on the control and confirmed
            f.NewRequest(Operator, "OPERATOR");
            MfOperation op = await f.Db.Set<MfOperation>().AsNoTracking().FirstAsync(o => o.Id == 1);
            var blocked = await MfDncGate.CheckStartAsync(f.Db, op, 1, MfBookingKind.Run, default);
            StringAssert.Contains(blocked.Error, "P-100");
            var terminal = await f.Dnc.GetTerminalAsync(new TerminalNcQuery { OperationId = 1, MachineId = 1 }, default);
            Ok(terminal, "terminal");
            Assert.IsTrue(terminal.Data.HasProgram && terminal.Data.Required && !terminal.Data.CanStart);

            // 5. the operator sends it; the queue delivers it to the converter with the line endings of the machine
            var sent = await f.Dnc.SendAsync(new SendNcDto { ProgramId = programId, MachineId = 1, OperationId = 1 }, default);
            Ok(sent, "send");
            Assert.AreEqual(MfNcTransferStatus.Queued, sent.Data.Status);
            Assert.AreEqual("100.H", sent.Data.TargetName);
            var again = await f.Dnc.SendAsync(new SendNcDto { ProgramId = programId, MachineId = 1, OperationId = 1 }, default);
            Assert.AreEqual(sent.Data.Id, again.Data.Id, "a double tap does not send twice");
            f.NewRequest(Approver, "PRODUCTIONMANAGER");
            var run = await f.Dnc.RunPendingAsync(default);
            Ok(run, "run pending");
            Assert.AreEqual(1, run.Data.Processed);
            Assert.AreEqual(1, run.Data.Delivered);
            byte[] onTheLine = f.Converter.WaitReceived();
            CollectionAssert.AreEqual(Ascii(GoodTnc), onTheLine, "the stored program already ends its lines with CR LF");
            f.NewRequest(Operator, "OPERATOR");
            NcTransferDto delivered = (await f.Dnc.GetTransfersAsync(new NcTransferQuery { MachineId = 1 }, default)).Data.Single();
            Assert.AreEqual(MfNcTransferStatus.Delivered, delivered.Status);
            Assert.AreEqual(MfNcRules.Sha256Hex(onTheLine), delivered.SentSha256);
            CollectionAssert.Contains(delivered.AllowedActions, "confirm");

            // 6. the operator checks it on the control and confirms; now the start passes and records the revision it runs by
            var confirmed = await f.Dnc.TransferActionAsync(new NcTransferActionDto { TransferId = delivered.Id, Action = "confirm", Note = "روی کنترلر بررسی شد" }, default);
            Ok(confirmed, "confirm");
            Assert.AreEqual(MfNcTransferStatus.Confirmed, confirmed.Data.Status);
            var pass = await MfDncGate.CheckStartAsync(f.Db, op, 1, MfBookingKind.Run, default);
            Assert.IsNull(pass.Error);
            MfNcRun passRun = pass.Runs.Single();
            Assert.AreEqual(r1.Data.Id, passRun.RevisionId);
            Assert.AreEqual(delivered.Id, passRun.TransferId);
            passRun.StartedAt = new DateTime(2026, 11, 8, 10, 0, 0);
            passRun.RecordedByUserId = Operator;
            f.Db.Set<MfNcRun>().Add(passRun);
            await f.Db.SaveChangesAsync();
            var runs = await f.Dnc.GetRunsAsync(new NcRunQuery { OperationId = 1 }, default);
            Ok(runs, "runs");
            Assert.AreEqual(1, runs.Data.Single().Revision);
            Assert.AreEqual("PO-1405-1/20 فرز کاری", runs.Data.Single().OperationName);
            Assert.IsNull(runs.Data.Single().EmployeeName, "an operator does not see names behind hours");

            // 7. a revision that does not fit the machine (B axis on a 3-axis machine) cannot be submitted, only withdrawn
            f.NewRequest(Programmer, "ENGINEERING");
            string badProgram = GoodTnc.Replace("5 L X+10 Y+10 R0 FMAX", "5 L X+10 Y+10 B+45 R0 FMAX");
            var r2 = await f.Dnc.UploadRevisionAsync(new UploadNcRevisionDto { ProgramId = programId, FileName = "flange.h", ContentBase64 = Convert.ToBase64String(Ascii(badProgram)) }, default);
            Ok(r2, "upload r2");
            Assert.AreEqual(1, r2.Data.ErrorCount);
            Assert.AreEqual("AXIS", r2.Data.Findings.Single().Code);
            Assert.AreEqual(1, r2.Data.DiffAdded);
            Assert.AreEqual(1, r2.Data.DiffRemoved);
            CollectionAssert.DoesNotContain(r2.Data.AllowedActions, "submit");
            Assert.IsFalse((await f.Dnc.RevisionActionAsync(new NcRevisionActionDto { RevisionId = r2.Data.Id, Action = "submit" }, default)).Success);
            var duplicate = await f.Dnc.UploadRevisionAsync(new UploadNcRevisionDto { ProgramId = programId, FileName = "flange.h", ContentBase64 = Convert.ToBase64String(Ascii(GoodTnc)) }, default);
            Assert.IsFalse(duplicate.Success, "one open revision at a time");
            Ok(await f.Dnc.RevisionActionAsync(new NcRevisionActionDto { RevisionId = r2.Data.Id, Action = "withdraw" }, default), "withdraw");

            // 8. the operator changed a feed at the control; the supervisor fetches the program back: a draft revision with its difference, authored by the one who asked (so he cannot approve it)
            string edited = GoodTnc.Replace("6 L Z-5 F300", "6 L Z-5 F250");
            f.Converter.PlayBack = Ascii(edited);
            f.NewRequest(Approver, "PRODUCTIONMANAGER");
            var asked = await f.Dnc.ReceiveAsync(new ReceiveNcDto { ProgramId = programId, MachineId = 1, ChangeNote = "اصلاح پیشروی پشت ماشین" }, default);
            Ok(asked, "receive");
            Assert.AreEqual("100.H", asked.Data.TargetName);
            var fetched = await f.Dnc.RunPendingAsync(default);
            Ok(fetched, "run pending (receive)");
            Assert.AreEqual(1, fetched.Data.Received, string.Join(" | ", fetched.Data.Messages) + " " + (await f.Dnc.GetTransfersAsync(new NcTransferQuery { Direction = MfNcTransferDirection.FromMachine }, default)).Data.Single().LastError);
            f.NewRequest(Approver, "PRODUCTIONMANAGER");
            NcProgramDto after = (await f.Dnc.GetProgramAsync(programId, default)).Data;
            NcRevisionDto r3 = after.Revisions.First();
            Assert.AreEqual(3, r3.Revision);
            Assert.AreEqual(MfNcRevisionSource.FromMachine, r3.Source);
            Assert.AreEqual(MfNcRevisionStatus.Draft, r3.Status);
            Assert.AreEqual(Approver, r3.AuthorUserId);
            Assert.AreEqual(r1.Data.Id, r3.BaseRevisionId);
            Assert.AreEqual(1, r3.DiffAdded);
            Assert.AreEqual(1, r3.DiffRemoved);
            Assert.AreEqual("اصلاح پیشروی پشت ماشین", r3.ChangeNote);
            var diff = await f.Dnc.GetRevisionDiffAsync(r3.Id, default);
            CollectionAssert.Contains(diff.Data.Lines, "- 7 6 L Z-5 F300");
            CollectionAssert.Contains(diff.Data.Lines, "+ 7 6 L Z-5 F250");
            Ok(await f.Dnc.RevisionActionAsync(new NcRevisionActionDto { RevisionId = r3.Id, Action = "submit" }, default), "submit r3");
            f.NewRequest(Approver, "PRODUCTIONMANAGER");
            var selfApprove = await f.Dnc.RevisionActionAsync(new NcRevisionActionDto { RevisionId = r3.Id, Action = "approve" }, default);
            Assert.IsFalse(selfApprove.Success, "who fetched it back cannot approve it");
            f.NewRequest(Programmer, "ENGINEERING");
            Ok(await f.Dnc.RevisionActionAsync(new NcRevisionActionDto { RevisionId = r3.Id, Action = "approve" }, default), "approve r3 by engineering");

            // 9. revision 3 is current now: the confirmation of revision 1 no longer covers the start, and the old delivery cannot be confirmed for it
            f.NewRequest(Operator, "OPERATOR");
            Assert.IsNotNull((await MfDncGate.CheckStartAsync(f.Db, op, 1, MfBookingKind.Run, default)).Error);
            NcFileDto content = (await f.Dnc.GetRevisionContentAsync(r3.Id, default)).Data;
            Assert.AreEqual(edited, Encoding.ASCII.GetString(content.Bytes));
            Assert.AreEqual(MfNcRules.Sha256Hex(Ascii(edited)), content.Sha256);
        }

        [TestMethod]
        public async Task A_Manual_Transfer_Is_Delivered_By_The_Download_And_A_Down_Machine_Gets_No_Program()
        {
            using var f = new Flow();
            ApplicationDbContext db = f.Db;
            db.Set<Machine>().Add(new Machine { Id = 4, Title = "CMZ TBI520", IsActive = true });
            db.Set<MfMachineInfo>().Add(new MfMachineInfo { Id = 4, MachineId = 4, Status = MfMachineStatus.Available });
            db.Set<MfMachineConnection>().Add(new MfMachineConnection
            {
                Id = 4, MachineId = 4, ControllerFamily = MfNcControllerFamily.Fanuc, LinkKind = MfNcLinkKind.Manual, RequirePercent = true, Axes = "XZ", IsLathe = true,
                ProgramNameFormat = "O{number}", LineEnding = MfNcLineEnding.CrLf, IsActive = true, RequireConfirmedProgram = true,
            });
            db.Set<MfNcProgram>().Add(new MfNcProgram { Id = 9, Code = "T-12", Title = "تراش فلنج", MachineId = 4, ControllerFamily = MfNcControllerFamily.Fanuc, Status = MfNcProgramStatus.Active, CurrentRevisionId = 90 });
            byte[] lathe = Ascii("%\nO0012\nG0 X0 Z0\nM30\n%\n");
            db.Set<MfNcRevision>().Add(new MfNcRevision { Id = 90, ProgramId = 9, Revision = 1, Status = MfNcRevisionStatus.Approved, FileName = "t12.nc", Sha256 = MfNcRules.Sha256Hex(lathe), ProgramNumber = "O0012", ApprovedAt = DateTimeOffset.Now.AddHours(-1) });
            db.Set<MfNcContent>().Add(new MfNcContent { Id = 90, RevisionId = 90, Bytes = lathe });
            await db.SaveChangesAsync();

            f.NewRequest(13, "OPERATOR");
            var sent = await f.Dnc.SendAsync(new SendNcDto { ProgramId = 9, MachineId = 4 }, default);
            Ok(sent, "send");
            Assert.AreEqual("O0012", sent.Data.TargetName);
            CollectionAssert.Contains(sent.Data.AllowedActions, "download");
            var file = await f.Dnc.GetTransferFileAsync(sent.Data.Id, default);
            Ok(file, "file");
            CollectionAssert.AreEqual(Ascii("%\r\nO0012\r\nG0 X0 Z0\r\nM30\r\n%\r\n"), file.Data.Bytes, "LF turned into the CR LF of the machine");
            NcTransferDto t = (await f.Dnc.GetTransfersAsync(new NcTransferQuery { MachineId = 4 }, default)).Data.Single();
            Assert.AreEqual(MfNcTransferStatus.Delivered, t.Status);
            Assert.AreEqual(file.Data.Bytes.Length, t.SentBytes);

            // the machine breaks down: no program goes to it
            f.NewRequest(1, "PRODUCTIONMANAGER");
            MfMachineInfo info = await f.Db.Set<MfMachineInfo>().FirstAsync(i => i.MachineId == 4);
            info.Status = MfMachineStatus.Down;
            await f.Db.SaveChangesAsync();
            Ok(await f.Dnc.TransferActionAsync(new NcTransferActionDto { TransferId = t.Id, Action = "cancel", Note = "خرابی ماشین" }, default), "cancel");
            var refused = await f.Dnc.SendAsync(new SendNcDto { ProgramId = 9, MachineId = 4 }, default);
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "خراب");
        }

        [TestMethod]
        public async Task A_Failed_Delivery_Is_Retried_Up_To_Three_Times_And_Then_Waits_For_A_Person()
        {
            using var f = new Flow();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int deadPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            ApplicationDbContext db = f.Db;
            db.Set<Machine>().Add(new Machine { Id = 2, Title = "Reiden", IsActive = true });
            db.Set<MfMachineConnection>().Add(new MfMachineConnection
            {
                Id = 2, MachineId = 2, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, LinkKind = MfNcLinkKind.SerialGateway, Host = "127.0.0.1", Port = deadPort, IsActive = true, RequireConfirmedProgram = true,
            });
            db.Set<MfNcProgram>().Add(new MfNcProgram { Id = 3, Code = "R-1", Title = "x", MachineId = 2, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, Status = MfNcProgramStatus.Active, CurrentRevisionId = 30 });
            byte[] p = Ascii(GoodTnc);
            db.Set<MfNcRevision>().Add(new MfNcRevision { Id = 30, ProgramId = 3, Revision = 1, Status = MfNcRevisionStatus.Approved, FileName = "100.h", Sha256 = MfNcRules.Sha256Hex(p), ProgramNumber = "100" });
            db.Set<MfNcContent>().Add(new MfNcContent { Id = 30, RevisionId = 30, Bytes = p });
            await db.SaveChangesAsync();

            f.NewRequest(13, "OPERATOR");
            Ok(await f.Dnc.SendAsync(new SendNcDto { ProgramId = 3, MachineId = 2 }, default), "send");
            f.NewRequest(1, "PRODUCTIONMANAGER");
            Assert.AreEqual(1, (await f.Dnc.ProcessPendingAsync(default)).Failed);
            for (int attempt = 2; attempt <= 3; attempt++)
            {
                f.NewRequest(1, "PRODUCTIONMANAGER");
                MfNcTransfer row = await f.Db.Set<MfNcTransfer>().FirstAsync();
                row.NextAttemptAt = DateTimeOffset.Now.AddSeconds(-1);   // the minute has passed
                await f.Db.SaveChangesAsync();
                Assert.AreEqual(1, (await f.Dnc.ProcessPendingAsync(default)).Failed, "attempt " + attempt);
            }
            f.NewRequest(1, "PRODUCTIONMANAGER");
            MfNcTransfer last = await f.Db.Set<MfNcTransfer>().AsNoTracking().FirstAsync();
            Assert.AreEqual(3, last.Attempts);
            Assert.IsNull(last.NextAttemptAt, "no fourth automatic attempt");
            Assert.AreEqual(0, (await f.Dnc.ProcessPendingAsync(default)).Processed);
            NcTransferDto dto = (await f.Dnc.GetTransfersAsync(new NcTransferQuery(), default)).Data.Single();
            CollectionAssert.Contains(dto.AllowedActions, "retry");
            StringAssert.Contains(dto.LastError, "اتصال برقرار نشد");
        }
    }
}
