using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production.Dnc;
using ViewModels.NasbNiro.ProductionDtos;

namespace MsTests
{
    /// <summary>Production release P2 part E: the guarded machining process (rules, LSV2 client, start interlock, gates, overrides, measurements, CAM evidence).</summary>
    [TestClass]
    public class GuardTests
    {
        // ================================================================================== rules

        [TestMethod]
        public void The_Plc_Map_Takes_Words_And_The_Program_State_Marker_Only()
        {
            var (map, error) = MfGuardRules.ParsePlcMap("exec=W100; state=M2000; feed=w104;spindle=W106");
            Assert.IsNull(error, error);
            Assert.AreEqual(('W', 100), map["exec"]);
            Assert.AreEqual(('M', 2000), map["state"]);
            Assert.AreEqual(('W', 104), map["feed"]);
            Assert.AreEqual(4, map.Count);
            StringAssert.Contains(MfGuardRules.ParsePlcMap("feed=M10").Error, "مارکر");
            StringAssert.Contains(MfGuardRules.ParsePlcMap("speed=W10").Error, "نامعتبر");
            Assert.AreEqual(0, MfGuardRules.ParsePlcMap(null).Map.Count);
        }

        [TestMethod]
        public void Two_Copies_Of_A_Program_Differ_Only_In_Content_Not_In_Spacing()
        {
            byte[] a = Encoding.ASCII.GetBytes("0 BEGIN PGM 100 MM\r\n1 L  X+10   Y+5 F500\r\n\r\n2 END PGM 100 MM\r\n");
            byte[] b = Encoding.ASCII.GetBytes("0 begin pgm 100 mm\n1 L X+10 Y+5 F500\n2 END PGM 100 MM");
            byte[] c = Encoding.ASCII.GetBytes("0 BEGIN PGM 100 MM\n1 L X+10 Y+5 F800\n2 END PGM 100 MM");
            Assert.AreEqual(MfGuardRules.CanonicalSha(a), MfGuardRules.CanonicalSha(b));
            Assert.AreNotEqual(MfGuardRules.CanonicalSha(a), MfGuardRules.CanonicalSha(c), "a changed feed is a different program");
            Assert.AreEqual(0, MfGuardRules.FirstDifference(a, b));
            Assert.AreEqual(2, MfGuardRules.FirstDifference(a, c));
            Assert.AreEqual(3, MfGuardRules.FirstDifference(a, Encoding.ASCII.GetBytes("0 BEGIN PGM 100 MM\n1 L X+10 Y+5 F500")), "a missing last block");
            Assert.AreEqual("TNC:\\nc_prog\\100.H", MfGuardRules.ProgramPath("TNC:/nc_prog", "100.H"));
            Assert.AreEqual("TNC:\\100.H", MfGuardRules.ProgramPath(null, "100.H"));
            Assert.IsFalse(MfGuardRules.IsControlPath("TNC:\\..\\SYS"));
            Assert.IsTrue(MfGuardRules.IsControlPath("TNC:\\nc_prog\\"));
        }

        private const string ToolTable =
            "BEGIN TOOL    .T  MM\r\n" +
            "T    NAME             L          R          DL     DR\r\n" +
            "1    END_MILL_10      75.000     5.000      0      0\r\n" +
            "2    DRILL_6          40.000     3.000      0      0\r\n" +
            "3    UNMEASURED       0          4.000      0      0\r\n" +
            "[END]\r\n";

        [TestMethod]
        public void The_Tool_Table_Is_Read_By_Its_Header_Columns_And_Checked_Against_The_Cam_List()
        {
            List<MfGuardRules.ToolRow> rows = MfGuardRules.ParseToolTable(ToolTable);
            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("END_MILL_10", rows[0].Name);
            Assert.AreEqual(75m, rows[0].L);
            Assert.AreEqual(3m, rows[1].R);

            var cam = new List<MfCamTool>
            {
                new MfCamTool { ToolNumber = 1, Name = "EM10", Diameter = 10m, Length = 70m },
                new MfCamTool { ToolNumber = 2, Diameter = 6m, Length = 45m },        // too short on the machine
                new MfCamTool { ToolNumber = 3, Diameter = 8m },                      // not measured
                new MfCamTool { ToolNumber = 4, Diameter = 12m },                     // not in the table
            };
            List<string> problems = MfGuardRules.CheckTools(cam, rows, 0.02m, 1m);
            Assert.AreEqual(3, problems.Count, string.Join(" | ", problems));
            StringAssert.Contains(problems[0], "کوتاه‌تر");
            StringAssert.Contains(problems[1], "اندازه‌گیری نشده");
            StringAssert.Contains(problems[2], "در جدول ابزار ماشین نیست");
            Assert.AreEqual(0, MfGuardRules.CheckTools(cam.Take(1), rows, 0.02m, 1m).Count);
            cam[0].Diameter = 10.1m;
            StringAssert.Contains(MfGuardRules.CheckTools(cam.Take(1), rows, 0.02m, 1m).Single(), "شعاع");
        }

        [TestMethod]
        public void The_Cam_Tool_List_And_The_Probe_Results_Are_Parsed()
        {
            var (tools, error) = MfGuardRules.ParseCamToolList("Tool;Name;Diameter;Length\n1;EM10;10;70\n2;DR6;6;45\n1;EM10;10;70\n");
            Assert.IsNull(error, error);
            Assert.AreEqual(2, tools.Count, "the same tool in two operations is one tool");
            Assert.AreEqual(6m, tools[1].Diameter);
            StringAssert.Contains(MfGuardRules.ParseCamToolList("Name,Diameter\nEM,10").Error, "ستون شماره ابزار");
            StringAssert.Contains(MfGuardRules.ParseCamToolList("T\nabc").Error, "سطر 2");

            var results = MfGuardRules.ParseResults("; probe cycle 431\r\nd1=10.012\r\nPOS_X ; -0.5\r\nnot a value\r\n");
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(("D1", 10.012m), results[0]);
            Assert.AreEqual(("POS_X", -0.5m), results[1]);
        }

        [TestMethod]
        public void A_Prove_Out_Is_Single_Block_Or_Reduced_Feed_While_The_Program_Runs()
        {
            DateTime t = new DateTime(2026, 11, 9, 9, 0, 0);
            MfGuardRules.Sample S(int s, MfExecMode? e = null, MfProgramState? p = null, int? f = null) => new MfGuardRules.Sample { At = t.AddSeconds(s), Exec = e, State = p, Feed = f };
            Assert.IsTrue(MfGuardRules.ProveOutObserved(new[] { S(0, MfExecMode.SingleBlock), S(5, p: MfProgramState.Running) }));
            Assert.IsTrue(MfGuardRules.ProveOutObserved(new[] { S(0, MfExecMode.Automatic, f: 100), S(5, p: MfProgramState.Running), S(9, f: 40) }));
            Assert.IsFalse(MfGuardRules.ProveOutObserved(new[] { S(0, MfExecMode.Automatic, f: 100), S(5, p: MfProgramState.Running), S(60, p: MfProgramState.Finished) }));
            Assert.IsFalse(MfGuardRules.ProveOutObserved(new[] { S(0, f: 30), S(5, MfExecMode.Automatic) }), "a low feed while nothing runs proves nothing");
        }

        // ================================================================================== LSV2 against a fake control

        /// <summary>A HEIDENHAIN control on the loopback speaking LSV2 (telegram: 4-byte length, 4 command letters, payload).</summary>
        private sealed class FakeTnc : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
            public bool DenyDnc { get; set; }
            public ushort Exec { get; set; } = 4;
            public ushort Program { get; set; } = 0;
            public uint Feed { get; set; } = 10000;
            public uint Spindle { get; set; } = 9500;
            public string Selected { get; set; } = "TNC:\\nc_prog\\100.H";
            public uint Tool { get; set; } = 12;
            public ConcurrentDictionary<string, string> Files { get; } = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public short[] Words { get; } = new short[200];
            public List<string> Commands { get; } = new List<string>();

            public FakeTnc()
            {
                _listener.Start();
                _ = Task.Run(AcceptAsync);
            }

            private async Task AcceptAsync()
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await _listener.AcceptTcpClientAsync(); } catch { return; }
                    _ = Task.Run(() => ServeAsync(c));
                }
            }

            private static async Task<byte[]> ReadAsync(NetworkStream s, int n)
            {
                var b = new byte[n];
                int r = 0;
                while (r < n) { int k = await s.ReadAsync(b.AsMemory(r)); if (k == 0) return null; r += k; }
                return b;
            }

            private static async Task SendAsync(NetworkStream s, string cmd, byte[] payload = null)
            {
                payload ??= Array.Empty<byte>();
                var t = new byte[8 + payload.Length];
                BinaryPrimitives.WriteUInt32BigEndian(t, (uint)payload.Length);
                Encoding.ASCII.GetBytes(cmd, 0, 4, t, 4);
                payload.CopyTo(t, 8);
                await s.WriteAsync(t);
            }

            private static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
            private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }

            private async Task ServeAsync(TcpClient c)
            {
                using (c)
                {
                    NetworkStream s = c.GetStream();
                    byte[] pending = null;
                    while (true)
                    {
                        byte[] head = await ReadAsync(s, 8);
                        if (head == null) return;
                        int len = (int)BinaryPrimitives.ReadUInt32BigEndian(head);
                        string cmd = Encoding.ASCII.GetString(head, 4, 4);
                        byte[] p = len == 0 ? Array.Empty<byte>() : await ReadAsync(s, len);
                        lock (Commands) Commands.Add(cmd);
                        string text = Encoding.Latin1.GetString(p).Split('\0')[0];
                        switch (cmd)
                        {
                            case "A_LG":
                                await SendAsync(s, DenyDnc && text == "DNC" ? "T_ER" : "T_OK", DenyDnc && text == "DNC" ? new byte[] { 1, 2 } : null);
                                break;
                            case "A_LO": return;
                            case "R_VR": await SendAsync(s, "S_VR", Encoding.Latin1.GetBytes("TNC640\0" + "340590 10\0PLC\0OPT\0")); break;
                            case "R_PR":
                            {
                                var b = new byte[124];
                                BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0), 0);        // markers start
                                BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), 10000);    // markers
                                BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(40), 20000);   // words start
                                BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(44), 200);     // words
                                BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(98), 1024);    // max block length
                                await SendAsync(s, "S_PR", b);
                                break;
                            }
                            case "C_CC": await SendAsync(s, "T_OK"); break;
                            case "R_RI":
                            {
                                ushort info = BinaryPrimitives.ReadUInt16BigEndian(p);
                                byte[] a = info switch
                                {
                                    23 => U16(Exec),
                                    26 => U16(Program),
                                    25 => U32(Feed).Concat(U32(Spindle)).Concat(U32(10000)).ToArray(),
                                    24 => U32(37).Concat(Encoding.Latin1.GetBytes(Selected + "\0" + Selected + "\0")).ToArray(),
                                    51 => U32(Tool).Concat(BitConverter.GetBytes(75.0)).Concat(BitConverter.GetBytes(5.0)).ToArray(),
                                    _ => null,
                                };
                                if (a == null) await SendAsync(s, "T_ER", new byte[] { 1, 1 }); else await SendAsync(s, "S_RI", a);
                                break;
                            }
                            case "R_FI":
                                if (Files.ContainsKey(text)) await SendAsync(s, "S_FI", U32((uint)Files[text].Length).Concat(U32(1_794_000_000)).ToArray());
                                else await SendAsync(s, "T_ER", new byte[] { 1, 3 });
                                break;
                            case "R_FL":
                                if (!Files.TryGetValue(text, out string content)) { await SendAsync(s, "T_ER", new byte[] { 1, 3 }); break; }
                                byte[] all = Encoding.Latin1.GetBytes(content.Replace("\r\n", "\0"));
                                // two blocks, to prove the client asks for the next one
                                int half = all.Length / 2;
                                pending = all.Skip(half).ToArray();
                                await SendAsync(s, "S_FL", all.Take(half).ToArray());
                                break;
                            case "T_OK":
                                if (pending != null) { byte[] next = pending; pending = null; await SendAsync(s, "S_FL", next); }
                                else await SendAsync(s, "T_FD");
                                break;
                            case "R_MB":
                            {
                                uint address = BinaryPrimitives.ReadUInt32BigEndian(p);
                                int count = p[4];
                                var b = new byte[count];
                                int word = (int)(address - 20000) / 2;
                                for (int i = 0; i < count / 2; i++) BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(i * 2), Words[word + i]);
                                await SendAsync(s, "S_MB", b);
                                break;
                            }
                            default: await SendAsync(s, "T_ER", new byte[] { 9, 9 }); break;
                        }
                    }
                }
            }

            public void Dispose() => _listener.Stop();
        }

        [TestMethod]
        public async Task The_Lsv2_Session_Reads_The_State_The_Program_And_The_Tool_Table_Of_The_Control()
        {
            using var tnc = new FakeTnc { Exec = 3, Program = 0, Feed = 4000 };
            tnc.Files["TNC:\\nc_prog\\100.H"] = "0 BEGIN PGM 100 MM\r\n1 TOOL CALL 1 Z S8000\r\n2 L X+10 Y+5 F500\r\n3 END PGM 100 MM\r\n";
            using (var s = await Lsv2ControlSession.OpenAsync("127.0.0.1", tnc.Port, null, null, default))
            {
                Assert.AreEqual("TNC640 / NC 340590 10", s.Identity);
                ControlSnapshot snap = await s.ReadAsync(default);
                Assert.AreEqual(MfExecMode.SingleBlock, snap.ExecMode);
                Assert.AreEqual(MfProgramState.Running, snap.ProgramState);
                Assert.AreEqual(40, snap.FeedOverride);
                Assert.AreEqual(95, snap.SpindleOverride);
                Assert.AreEqual("TNC:\\nc_prog\\100.H", snap.SelectedProgram);
                Assert.AreEqual(37, snap.BlockNumber);
                Assert.AreEqual(12, snap.ToolNumber);
                byte[] file = await s.ReadFileAsync("TNC:\\nc_prog\\100.H", 1 << 20, default);
                Assert.AreEqual(tnc.Files["TNC:\\nc_prog\\100.H"], Encoding.Latin1.GetString(file), "NUL line ends come back as CR LF, over two blocks");
                Assert.IsNull(await s.ReadFileAsync("TNC:\\nc_prog\\999.H", 1 << 20, default));
                Assert.IsNotNull(await s.FileTimeAsync("TNC:\\nc_prog\\100.H", default));
                Assert.IsNull(await s.FileTimeAsync("TNC:\\missing.h", default));
            }
            Assert.IsTrue(tnc.Commands.Contains("C_CC"), "the bigger buffer was asked for");
            await Task.Delay(50);
            Assert.AreEqual("A_LO", tnc.Commands.Last(), "the session logs out");
        }

        [TestMethod]
        public async Task An_Older_Control_Without_The_Dnc_Login_Is_Read_Through_The_Plc_Map()
        {
            using var tnc = new FakeTnc { DenyDnc = true };
            tnc.Words[100] = 4;     // automatic
            tnc.Words[102] = 0;     // started
            tnc.Words[104] = 80;    // feed override
            tnc.Words[108] = 7;     // tool
            using var s = await Lsv2ControlSession.OpenAsync("127.0.0.1", tnc.Port, "exec=W100;state=W102;feed=W104;tool=W108", null, default);
            ControlSnapshot snap = await s.ReadAsync(default);
            Assert.AreEqual(MfExecMode.Automatic, snap.ExecMode);
            Assert.AreEqual(MfProgramState.Running, snap.ProgramState);
            Assert.AreEqual(80, snap.FeedOverride);
            Assert.AreEqual(7, snap.ToolNumber);
            Assert.IsNull(snap.SelectedProgram, "not mapped, not answered");
            List<string> caps = await s.CapabilitiesAsync(null, default);
            Assert.IsTrue(caps.Any(l => l.StartsWith("✘ برنامه انتخاب‌شده")), string.Join(" | ", caps));
            Assert.IsTrue(caps.Any(l => l.StartsWith("✔ حالت کار")), string.Join(" | ", caps));
        }

        [TestMethod]
        public async Task The_Coil_Writer_Sends_Function_Five_And_Checks_The_Echo()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            byte[] seen = null;
            Task server = Task.Run(async () =>
            {
                using TcpClient c = await listener.AcceptTcpClientAsync();
                NetworkStream s = c.GetStream();
                var req = new byte[12];
                int r = 0;
                while (r < 12) r += await s.ReadAsync(req.AsMemory(r));
                seen = req;
                await s.WriteAsync(req);   // a module echoes the request
            });
            try
            {
                string error = await new ModbusCoilWriter().WriteAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, 3, true, default);
                await server;
                Assert.IsNull(error, error);
                Assert.AreEqual(5, seen[7]);
                Assert.AreEqual(3, seen[9]);
                Assert.AreEqual(0xFF, seen[10]);
            }
            finally { listener.Stop(); }
            StringAssert.Contains(await new ModbusCoilWriter().WriteAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, 3, false, default), "اتصال برقرار نشد");
        }

        // ================================================================================== the service

        private sealed class User : IInventoryUserContext
        {
            public int UserId { get; set; } = 1;
            public string Roles { get; set; } = "OPERATOR";
            public bool IsInRole(string role) => (role ?? "").Split(',').Select(r => r.Trim()).Any(r => Roles.Split(',').Contains(r));
        }

        private sealed class FakeSession : IMfControlSession
        {
            public FakeControl Control { get; init; }
            public string Identity => "TNC640";
            public Task<ControlSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(Control.Snapshot);
            public Task<byte[]> ReadFileAsync(string path, long maxBytes, CancellationToken ct) =>
                Task.FromResult(Control.Files.TryGetValue(path, out string t) ? Encoding.Latin1.GetBytes(t) : null);
            public Task<DateTimeOffset?> FileTimeAsync(string path, CancellationToken ct) =>
                Task.FromResult(Control.FileTimes.TryGetValue(path, out DateTimeOffset t) ? (DateTimeOffset?)t : null);
            public Task<List<string>> CapabilitiesAsync(string programFolder, CancellationToken ct) => Task.FromResult(new List<string> { "✔ حالت کار" });
            public void Dispose() { }
        }

        private sealed class FakeControl : IMfControlLinkFactory
        {
            public bool Down { get; set; }
            public ControlSnapshot Snapshot { get; set; } = new ControlSnapshot { ExecMode = MfExecMode.Manual, ProgramState = MfProgramState.Idle, FeedOverride = 100, SpindleOverride = 100 };
            public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, DateTimeOffset> FileTimes { get; } = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            public Task<IMfControlSession> OpenAsync(MfMachineMonitor monitor, string plcPassword, CancellationToken ct) =>
                Down ? throw new Lsv2Exception("کنترلر پاسخ نداد.") : Task.FromResult<IMfControlSession>(new FakeSession { Control = this });
        }

        private sealed class FakeCoils : IMfCoilWriter
        {
            public List<(int Coil, bool On)> Writes { get; } = new List<(int, bool)>();
            public Task<string> WriteAsync(string host, int port, byte unit, int coil, bool on, CancellationToken ct) { Writes.Add((coil, on)); return Task.FromResult<string>(null); }
        }

        private const string Program100 = "0 BEGIN PGM 100 MM\r\n1 TOOL CALL 1 Z S8000\r\n2 L X+10 Y+5 F500\r\n3 TOOL CALL 2 Z S3000\r\n4 CYCL DEF 200\r\n5 END PGM 100 MM\r\n";

        private sealed class Rig : IDisposable
        {
            public ServiceProvider Provider { get; }
            public User User { get; } = new User();
            public FakeControl Control { get; } = new FakeControl();
            public FakeCoils Coils { get; } = new FakeCoils();
            public DateTime Now { get; set; } = new DateTime(2026, 11, 9, 8, 0, 0);
            public IServiceScope Scope { get; private set; }
            public ApplicationDbContext Db => Scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            public int MachineId { get; }

            public MfGuardService Guard
            {
                get { var g = Scope.ServiceProvider.GetRequiredService<MfGuardService>(); g.Clock = () => Now; return g; }
            }

            public Rig(int machineId)
            {
                MachineId = machineId;
                Provider = CeoOverviewServiceTests.InMemory(s =>
                {
                    s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["Dnc:AllowLoopback"] = "true" }).Build());
                    s.AddScoped<IInventoryUserContext>(_ => User);
                    s.AddSingleton<IMfControlLinkFactory>(Control);
                    s.AddSingleton<IMfCoilWriter>(Coils);
                    s.AddScoped<MfGuardService>();
                    s.AddScoped<MfMeasureService>();
                    s.AddScoped<MfChecklistService>();
                    s.AddScoped<MfCamEvidenceService>();
                });
                Scope = Provider.CreateScope();
            }

            public void NewRequest(int userId = 1, string roles = "OPERATOR")
            {
                Scope.Dispose();
                Scope = Provider.CreateScope();
                User.UserId = userId;
                User.Roles = roles;
            }

            public void Dispose() { Scope.Dispose(); Provider.Dispose(); }
        }

        private static async Task SeedAsync(Rig rig)
        {
            var db = rig.Db;
            DateTimeOffset at = new DateTimeOffset(rig.Now.AddDays(-1));
            db.Set<Machine>().Add(new Machine { Id = rig.MachineId, Title = "DMU 50", IsActive = true });
            db.Set<MfMachineMonitor>().Add(new MfMachineMonitor
            {
                MachineId = rig.MachineId, Source = MfMonitorSource.ControlLink, IsActive = true, MismatchMinutes = 15, DataLink = MfDataLinkKind.Lsv2Tcp, DataHost = "127.0.0.1", ProgramFolder = "TNC:\\nc_prog\\",
                ResultFile = "TNC:\\RESULT.TXT", InterlockEnabled = true, InterlockHost = "127.0.0.1", InterlockPort = 502, InterlockCoil = 3, CreateDate = at, ModifyDate = at,
            });
            db.Set<MfNcProgram>().Add(new MfNcProgram { Id = 1, Code = "P-100", Title = "فرز بدنه", MachineId = rig.MachineId, OperationTemplateId = 5, ControllerFamily = MfNcControllerFamily.HeidenhainConversational, Status = MfNcProgramStatus.Active, CurrentRevisionId = 10, CreateDate = at, ModifyDate = at });
            byte[] bytes = Encoding.ASCII.GetBytes(Program100);
            db.Set<MfNcRevision>().Add(new MfNcRevision { Id = 10, ProgramId = 1, Revision = 1, Status = MfNcRevisionStatus.Approved, FileName = "100.h", Sha256 = MfNcRules.Sha256Hex(bytes), SizeBytes = bytes.Length, ApprovedAt = at, CreateDate = at, ToolCount = 2 });
            db.Set<MfNcContent>().Add(new MfNcContent { Id = 10, RevisionId = 10, Bytes = bytes });
            db.Set<MfNcTransfer>().Add(new MfNcTransfer { Id = 5, Direction = MfNcTransferDirection.ToMachine, MachineId = rig.MachineId, ProgramId = 1, RevisionId = 10, TargetName = "100.H", Status = MfNcTransferStatus.Confirmed, RequestedAt = at, DeliveredAt = at, ConfirmedAt = at, CreateDate = at, ModifyDate = at });
            db.Set<MfCamTool>().Add(new MfCamTool { RevisionId = 10, ToolNumber = 1, Name = "EM10", Diameter = 10m, Length = 70m });
            db.Set<MfCamTool>().Add(new MfCamTool { RevisionId = 10, ToolNumber = 2, Name = "DR6", Diameter = 6m, Length = 35m });
            foreach (int id in new[] { 50, 51, 52, 53, 54 })
                db.Set<MfOperation>().Add(new MfOperation { Id = id, OrderId = 1, Sequence = 10, Title = "فرز", WorkCenterId = 1, OperationTemplateId = 5, Status = MfOperationStatus.Ready });
            await db.SaveChangesAsync();
            rig.Control.Files["TNC:\\nc_prog\\100.H"] = Program100.ToLowerInvariant().Replace(" X+10", "  X+10");   // the control re-spaced it
            rig.Control.Files["TNC:\\TOOL.T"] = ToolTable;
        }

        private static MfNcRun Run(Rig rig, int operationId) => new MfNcRun { OperationId = operationId, MachineId = rig.MachineId, ProgramId = 1, RevisionId = 10, TransferId = 5, Kind = MfBookingKind.Run };

        /// <summary>What the terminal start does after the gates: the run and the open booking in one save.</summary>
        private static async Task<MfNcRun> StartAsync(Rig rig, int operationId)
        {
            MfNcRun run = Run(rig, operationId);
            string error = await rig.Guard.CheckStartAsync(await rig.Db.Set<MfOperation>().FirstAsync(o => o.Id == operationId), rig.MachineId, MfBookingKind.Run, 9, new List<MfNcRun> { run }, default);
            Assert.IsNull(error, error);
            run.EmployeeId = 9; run.StartedAt = rig.Now; run.RecordedByUserId = 1;
            rig.Db.Set<MfNcRun>().Add(run);
            rig.Db.Set<MfTimeBooking>().Add(new MfTimeBooking { OperationId = operationId, EmployeeId = 9, MachineId = rig.MachineId, Kind = MfBookingKind.Run, StartAt = rig.Now, Source = MfBookingSource.Terminal, CreateDate = DateTimeOffset.Now, ModifyDate = DateTimeOffset.Now });
            await rig.Db.SaveChangesAsync();
            return run;
        }

        private static async Task CloseBookingsAsync(Rig rig)
        {
            foreach (MfTimeBooking b in await rig.Db.Set<MfTimeBooking>().Where(b => b.EndAt == null).ToListAsync()) b.EndAt = rig.Now;
            await rig.Db.SaveChangesAsync();
        }

        [TestMethod]
        public async Task The_Guarded_Start_Reads_The_Program_And_Tools_Back_Drives_The_Interlock_And_Asks_For_The_Proof()
        {
            using var rig = new Rig(701);
            await SeedAsync(rig);

            // nothing booked: the start is locked
            rig.NewRequest();
            await rig.Guard.SyncInterlockAsync(rig.MachineId, default);
            Assert.AreEqual((3, false), rig.Coils.Writes.Last());

            // op 50: program and tools match; the first run of the revision must be proven
            rig.NewRequest();
            MfNcRun run50 = await StartAsync(rig, 50);
            Assert.AreEqual(MfGuardRules.CanonicalSha(Encoding.ASCII.GetBytes(Program100)), run50.VerifiedSha256);
            Assert.IsTrue(run50.ToolsVerified);
            Assert.IsTrue(run50.ProveOut);
            rig.NewRequest();
            await rig.Guard.SyncInterlockAsync(rig.MachineId, default);
            Assert.AreEqual((3, true), rig.Coils.Writes.Last(), "a guarded booking frees the start button");
            var kinds = await rig.Db.Set<MfMachineActivity>().Where(a => a.OperationId == 50).Select(a => a.Kind).ToListAsync();
            CollectionAssert.IsSubsetOf(new[] { MfActivityKind.ProgramVerified, MfActivityKind.ToolsVerified, MfActivityKind.Interlock }, kinds);

            // the operator runs it in automatic at full feed: recorded and told at once
            rig.Now = rig.Now.AddMinutes(2);
            rig.Control.Snapshot = new ControlSnapshot { ExecMode = MfExecMode.Automatic, ProgramState = MfProgramState.Running, FeedOverride = 100, SpindleOverride = 100, SelectedProgram = "TNC:\\nc_prog\\100.H", ToolNumber = 1 };
            rig.NewRequest();
            GuardPollResultDto poll = await rig.Guard.PollAsync(default);
            Assert.AreEqual(1, poll.Polled);
            Assert.AreEqual(0, poll.Unapproved);
            rig.NewRequest();
            Assert.IsTrue(await rig.Db.Set<MfMachineActivity>().AnyAsync(a => a.Kind == MfActivityKind.ProveOut && a.Value == "missing" && a.OperationId == 50));
            Assert.AreEqual(MfMachineState.Running, (await rig.Db.Set<MfMachineStateEvent>().SingleAsync(e => e.MachineId == rig.MachineId && e.EndAt == null)).State, "the state comes from the control");

            // the program ends; the probe cycle wrote its results
            rig.Now = rig.Now.AddMinutes(10);
            rig.Control.Snapshot = new ControlSnapshot { ExecMode = MfExecMode.Automatic, ProgramState = MfProgramState.Finished, FeedOverride = 100, SelectedProgram = "TNC:\\nc_prog\\100.H" };
            rig.Control.Files["TNC:\\RESULT.TXT"] = "D1=10.008\r\nD2=6.004\r\n";
            rig.Control.FileTimes["TNC:\\RESULT.TXT"] = new DateTimeOffset(rig.Now);
            rig.NewRequest();
            Assert.AreEqual(2, (await rig.Guard.PollAsync(default)).Measurements);
            rig.NewRequest();
            Assert.AreEqual(10.008m, (await rig.Db.Set<MfMeasurement>().SingleAsync(m => m.OperationId == 50 && m.Characteristic == "D1")).Value);

            // the finish asks for the proof; only the production manager opens it once, with a reason
            rig.NewRequest();
            StringAssert.Contains(await rig.Guard.CheckFinishAsync(await rig.Db.Set<MfOperation>().FirstAsync(o => o.Id == 50), default), "مدیر تولید");
            rig.NewRequest(2, "SHOPSUPERVISOR");
            Assert.IsFalse((await rig.Guard.CreateOverrideAsync(new CreateGateOverrideDto { MachineId = rig.MachineId, OperationId = 50, Gate = "prove-out", Reason = "نمونه اول کنترل شد و سالم است" }, default)).Success, "a supervisor cannot");
            rig.NewRequest(3, "PRODUCTIONMANAGER");
            Assert.IsFalse((await rig.Guard.CreateOverrideAsync(new CreateGateOverrideDto { MachineId = rig.MachineId, OperationId = 50, Gate = "prove-out", Reason = "کوتاه" }, default)).Success, "the reason is written in full");
            var ov = await rig.Guard.CreateOverrideAsync(new CreateGateOverrideDto { MachineId = rig.MachineId, OperationId = 50, Gate = "prove-out", Reason = "قطعه اول با CMM کنترل و پذیرفته شد" }, default);
            Assert.IsTrue(ov.Success, ov.Message);
            rig.NewRequest();
            Assert.IsNull(await rig.Guard.CheckFinishAsync(await rig.Db.Set<MfOperation>().FirstAsync(o => o.Id == 50), default));
            await rig.Db.SaveChangesAsync();
            rig.NewRequest();
            MfNcRun saved50 = await rig.Db.Set<MfNcRun>().SingleAsync(r => r.OperationId == 50);
            Assert.AreEqual(false, saved50.ProveOutObserved);
            Assert.AreEqual(ov.Data.Id, saved50.OverrideId);
            Assert.IsNotNull((await rig.Db.Set<MfGateOverride>().SingleAsync()).ConsumedAt, "used once");
            await CloseBookingsAsync(rig);

            // op 51: still not proven, so proven now in single block
            rig.Now = rig.Now.AddMinutes(5);
            rig.Control.Snapshot = new ControlSnapshot { ExecMode = MfExecMode.Manual, ProgramState = MfProgramState.Idle, FeedOverride = 100, SelectedProgram = "TNC:\\nc_prog\\100.H" };
            rig.NewRequest();
            await rig.Guard.PollAsync(default);
            rig.NewRequest();
            MfNcRun run51 = await StartAsync(rig, 51);
            Assert.IsTrue(run51.ProveOut);
            rig.Now = rig.Now.AddMinutes(1);
            rig.Control.Snapshot = new ControlSnapshot { ExecMode = MfExecMode.SingleBlock, ProgramState = MfProgramState.Running, FeedOverride = 100, SelectedProgram = "TNC:\\nc_prog\\100.H" };
            rig.NewRequest();
            await rig.Guard.PollAsync(default);
            rig.Now = rig.Now.AddMinutes(20);
            rig.NewRequest();
            Assert.IsNull(await rig.Guard.CheckFinishAsync(await rig.Db.Set<MfOperation>().FirstAsync(o => o.Id == 51), default));
            await rig.Db.SaveChangesAsync();
            rig.NewRequest();
            Assert.AreEqual(true, (await rig.Db.Set<MfNcRun>().SingleAsync(r => r.OperationId == 51)).ProveOutObserved);
            await CloseBookingsAsync(rig);

            // op 52: the revision is proven on this machine
            rig.NewRequest();
            Assert.IsFalse((await StartAsync(rig, 52)).ProveOut);
            await CloseBookingsAsync(rig);
        }

        [TestMethod]
        public async Task A_Changed_Program_Wrong_Tools_Or_A_Lost_Link_Refuse_The_Start()
        {
            using var rig = new Rig(702);
            await SeedAsync(rig);
            MfOperation Op(int id) => rig.Db.Set<MfOperation>().First(o => o.Id == id);

            // the feed was edited on the control
            rig.Control.Files["TNC:\\nc_prog\\100.H"] = Program100.Replace("F500", "F900");
            rig.NewRequest();
            string error = await rig.Guard.CheckStartAsync(Op(53), rig.MachineId, MfBookingKind.Run, 9, new List<MfNcRun> { Run(rig, 53) }, default);
            StringAssert.Contains(error, "سطر 3");
            rig.NewRequest();
            Assert.IsTrue(await rig.Db.Set<MfMachineActivity>().AnyAsync(a => a.Kind == MfActivityKind.GateRefused && a.Value == "program" && a.OperationId == 53), "the refusal is on the time line");
            Assert.IsTrue(await rig.Db.Set<MfMachineActivity>().AnyAsync(a => a.Kind == MfActivityKind.ProgramMismatch));

            // the program is not on the control at all
            rig.Control.Files.Remove("TNC:\\nc_prog\\100.H");
            rig.NewRequest();
            StringAssert.Contains(await rig.Guard.CheckStartAsync(Op(53), rig.MachineId, MfBookingKind.Run, 9, new List<MfNcRun> { Run(rig, 53) }, default), "روی کنترلر نیست");

            // the right program, a drill too short on the machine; the production manager lets it go once
            rig.Control.Files["TNC:\\nc_prog\\100.H"] = Program100;
            rig.Control.Files["TNC:\\TOOL.T"] = ToolTable.Replace("40.000     3.000", "30.000     3.000");
            rig.NewRequest();
            StringAssert.Contains(await rig.Guard.CheckStartAsync(Op(53), rig.MachineId, MfBookingKind.Run, 9, new List<MfNcRun> { Run(rig, 53) }, default), "کوتاه‌تر");
            rig.NewRequest(3, "PRODUCTIONMANAGER");
            Assert.IsTrue((await rig.Guard.CreateOverrideAsync(new CreateGateOverrideDto { MachineId = rig.MachineId, OperationId = 53, Gate = "tools", Reason = "مته بلندتر جایگزین شد و طول آن دستی چک شد" }, default)).Success);
            Assert.IsFalse((await rig.Guard.CreateOverrideAsync(new CreateGateOverrideDto { MachineId = rig.MachineId, OperationId = 53, Gate = "tools", Reason = "مجوز دوم برای همان گیت داده نمی‌شود" }, default)).Success);
            rig.NewRequest();
            MfNcRun run = Run(rig, 53);
            Assert.IsNull(await rig.Guard.CheckStartAsync(Op(53), rig.MachineId, MfBookingKind.Run, 9, new List<MfNcRun> { run }, default));
            Assert.AreEqual(false, run.ToolsVerified);
            Assert.IsNotNull(run.OverrideId);
            await rig.Db.SaveChangesAsync();
            rig.NewRequest();
            StringAssert.Contains(await rig.Guard.CheckStartAsync(Op(53), rig.MachineId, MfBookingKind.Run, 9, new List<MfNcRun> { Run(rig, 53) }, default), "کوتاه‌تر", "the override was used once");

            // an override expires after half an hour
            rig.NewRequest(3, "PRODUCTIONMANAGER");
            Assert.IsTrue((await rig.Guard.CreateOverrideAsync(new CreateGateOverrideDto { MachineId = rig.MachineId, OperationId = 53, Gate = "tools", Reason = "مته بلندتر جایگزین شد و طول آن دستی چک شد" }, default)).Success);
            rig.Now = rig.Now.AddMinutes(31);
            rig.NewRequest();
            StringAssert.Contains(await rig.Guard.CheckStartAsync(Op(53), rig.MachineId, MfBookingKind.Run, 9, new List<MfNcRun> { Run(rig, 53) }, default), "کوتاه‌تر");

            // the link is down
            rig.Control.Down = true;
            rig.NewRequest();
            StringAssert.Contains(await rig.Guard.CheckStartAsync(Op(53), rig.MachineId, MfBookingKind.Run, 9, new List<MfNcRun> { Run(rig, 53) }, default), "ارتباط داده");

            // a machine without a data link is not guarded
            rig.NewRequest();
            Assert.IsNull(await rig.Guard.CheckStartAsync(Op(53), 9999, MfBookingKind.Run, 9, new List<MfNcRun>(), default));
            Assert.IsNull(await rig.Guard.CheckStartAsync(Op(53), rig.MachineId, MfBookingKind.Waiting, 9, new List<MfNcRun>(), default), "a waiting booking runs nothing");
        }

        [TestMethod]
        public async Task An_Unapproved_Program_Is_Flagged_And_Locks_The_Start()
        {
            using var rig = new Rig(703);
            await SeedAsync(rig);
            rig.NewRequest();
            await StartAsync(rig, 54);
            rig.NewRequest();
            await rig.Guard.SyncInterlockAsync(rig.MachineId, default);
            Assert.AreEqual((3, true), rig.Coils.Writes.Last());

            rig.Now = rig.Now.AddMinutes(1);
            rig.Control.Snapshot = new ControlSnapshot { ExecMode = MfExecMode.Automatic, ProgramState = MfProgramState.Running, FeedOverride = 40, SelectedProgram = "TNC:\\nc_prog\\999.H" };
            rig.NewRequest();
            GuardPollResultDto r = await rig.Guard.PollAsync(default);
            Assert.AreEqual(1, r.Unapproved);
            Assert.AreEqual((3, false), rig.Coils.Writes.Last(), "the start is locked while it is looked into");
            rig.NewRequest();
            MfMachineMonitor m = await rig.Db.Set<MfMachineMonitor>().SingleAsync(x => x.MachineId == rig.MachineId);
            Assert.IsFalse(m.StartEnabled);
            StringAssert.Contains(m.StartBlockReason, "تأییدنشده");
            Assert.AreEqual("TNC:\\nc_prog\\999.H", (await rig.Db.Set<MfMachineActivity>().SingleAsync(a => a.Kind == MfActivityKind.UnapprovedProgram)).Value);

            // the time line of the machine, newest first
            rig.NewRequest(4, "SHOPSUPERVISOR");
            var line = await rig.Guard.GetActivityAsync(new MachineActivityQuery { MachineId = rig.MachineId }, default);
            Assert.IsTrue(line.Success, line.Message);
            Assert.AreEqual(MfActivityKind.Interlock, line.Data[0].Kind);
            Assert.AreEqual("bad", line.Data.First(a => a.Kind == MfActivityKind.UnapprovedProgram).Tone);
        }

        [TestMethod]
        public async Task A_Device_Only_Item_Takes_Its_Value_From_The_Gauge_Never_From_The_Keyboard()
        {
            using var rig = new Rig(704);
            await SeedAsync(rig);
            rig.Db.Set<OperationTemplate>().Add(new OperationTemplate { Id = 5, Title = "فرز" });
            await rig.Db.SaveChangesAsync();

            rig.NewRequest(5, "QC");
            var bad = await rig.Scope.ServiceProvider.GetRequiredService<MfChecklistService>().SaveTemplateAsync(new ChecklistTemplateDto
            {
                OperationTemplateId = 5, Title = "کنترل قطر", RequiredToFinish = true, IsActive = true,
                Items = new List<ChecklistItemDto> { new ChecklistItemDto { Text = "قطر سوراخ", Kind = MfChecklistItemKind.YesNo, DeviceOnly = true, Characteristic = "D1" } },
            }, default);
            Assert.IsFalse(bad.Success, "device only is for a measured item");
            var t = await rig.Scope.ServiceProvider.GetRequiredService<MfChecklistService>().SaveTemplateAsync(new ChecklistTemplateDto
            {
                OperationTemplateId = 5, Title = "کنترل قطر", RequiredToFinish = true, IsActive = true,
                Items = new List<ChecklistItemDto> { new ChecklistItemDto { Text = "قطر سوراخ", Kind = MfChecklistItemKind.Measure, Min = 9.98m, Max = 10.02m, Unit = "mm", DeviceOnly = true, Characteristic = "d1" } },
            }, default);
            Assert.IsTrue(t.Success, t.Message);
            int item = t.Data.Items[0].Id.Value;
            Assert.AreEqual("D1", t.Data.Items[0].Characteristic);

            // the operator types a value: not taken
            rig.NewRequest(9, "OPERATOR");
            var typed = await rig.Scope.ServiceProvider.GetRequiredService<MfChecklistService>().SaveAnswersAsync(new SaveChecklistAnswersDto { OperationId = 50, Submit = true, Answers = new List<ChecklistAnswerDto> { new ChecklistAnswerDto { ItemId = item, Value = 10m } } }, default);
            Assert.IsFalse(typed.Success);

            // the gauge station pushes with its key from its address
            rig.NewRequest(1, "PRODUCTIONMANAGER");
            var measure = rig.Scope.ServiceProvider.GetRequiredService<MfMeasureService>();
            Assert.IsFalse((await measure.SaveStationAsync(new MeasureStationDto { Code = "G-01", Title = "گیج ایستگاه فرز", MachineId = rig.MachineId }, default)).Success, "an address list is required");
            var st = await measure.SaveStationAsync(new MeasureStationDto { Code = "G-01", Title = "گیج ایستگاه فرز", AllowedAddresses = "10.0.5.0/24", MachineId = rig.MachineId, IsActive = true }, default);
            Assert.IsTrue(st.Success, st.Message);
            string key = (await measure.IssueKeyAsync(st.Data.Id.Value, default)).Data.Key;
            rig.NewRequest(0, "");
            measure = rig.Scope.ServiceProvider.GetRequiredService<MfMeasureService>();
            var push = new MeasurementPushDto { Items = new List<MeasurementPushItemDto> { new MeasurementPushItemDto { OperationId = 50, Characteristic = "D1", Value = 10.011m, Device = "bore gauge 3" } } };
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await measure.PushAsync("G-01", "wrong", "10.0.5.9", push, default)).HttpStatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await measure.PushAsync("G-01", key, "10.0.6.9", push, default)).HttpStatusCode, "another address");
            var ok = await measure.PushAsync("G-01", key, "10.0.5.9", push, default);
            Assert.IsTrue(ok.Success, ok.Message);

            rig.NewRequest(9, "OPERATOR");
            var answered = await rig.Scope.ServiceProvider.GetRequiredService<MfChecklistService>().SaveAnswersAsync(new SaveChecklistAnswersDto { OperationId = 50, Submit = true, Answers = new List<ChecklistAnswerDto> { new ChecklistAnswerDto { ItemId = item, Value = 12m } } }, default);
            Assert.IsTrue(answered.Success, answered.Message);
            ChecklistAnswerDto a = answered.Data.Answers.Single();
            Assert.AreEqual(10.011m, a.Value, "the gauge's value, not the typed one");
            Assert.IsNotNull(a.MeasurementId);
            Assert.IsTrue(a.Passed);
        }

        [TestMethod]
        public async Task A_Revision_Needs_An_Approved_Post_A_Simulation_And_A_Tool_List()
        {
            using var rig = new Rig(705);
            await SeedAsync(rig);
            rig.Db.Set<MfNcRevision>().Add(new MfNcRevision { Id = 11, ProgramId = 1, Revision = 2, Status = MfNcRevisionStatus.Draft, FileName = "100.h", Sha256 = "x", CreateDate = DateTimeOffset.Now, CreatorUserId = 1 });
            await rig.Db.SaveChangesAsync();

            rig.NewRequest(1, "ENGINEERING");
            var cam = rig.Scope.ServiceProvider.GetRequiredService<MfCamEvidenceService>();
            var r0 = await cam.GetRevisionAsync(11, default);
            Assert.AreEqual(3, r0.Data.Missing.Count, string.Join(" | ", r0.Data.Missing));
            var post = await cam.CreatePostAsync(new SavePostProcessorDto { Code = "HH-TNC640", Title = "پست هایدن‌هاین ۵ محور", CamSystem = "PowerMill", ControllerFamily = MfNcControllerFamily.HeidenhainConversational, FileName = "tnc640.pmoptz", ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("post body")) }, default);
            Assert.IsTrue(post.Success, post.Message);
            Assert.AreEqual(1, post.Data.Version);
            Assert.IsFalse((await cam.SetRevisionAsync(new SetRevisionCamDto { RevisionId = 11, PostProcessorId = post.Data.Id, CamSystem = "PowerMill" }, default)).Success, "a draft post cannot be used");
            post = await cam.PostActionAsync(new PostProcessorActionDto { Id = post.Data.Id, Action = "submit", RowVersion = post.Data.RowVersion }, default);
            Assert.IsTrue(post.Success, post.Message);
            Assert.IsFalse((await cam.PostActionAsync(new PostProcessorActionDto { Id = post.Data.Id, Action = "approve", RowVersion = post.Data.RowVersion }, default)).Success, "never its own author");
            rig.NewRequest(2, "PRODUCTIONMANAGER");
            cam = rig.Scope.ServiceProvider.GetRequiredService<MfCamEvidenceService>();
            post = await cam.PostActionAsync(new PostProcessorActionDto { Id = post.Data.Id, Action = "approve", RowVersion = post.Data.RowVersion }, default);
            Assert.IsTrue(post.Success, post.Message);
            Assert.AreEqual(MfPostStatus.Approved, post.Data.Status);

            rig.NewRequest(1, "ENGINEERING");
            cam = rig.Scope.ServiceProvider.GetRequiredService<MfCamEvidenceService>();
            Assert.IsTrue((await cam.SetRevisionAsync(new SetRevisionCamDto { RevisionId = 11, PostProcessorId = post.Data.Id, CamSystem = "PowerMill" }, default)).Success);
            Assert.IsTrue((await cam.UploadAttachmentAsync(new UploadNcAttachmentDto { RevisionId = 11, Kind = MfNcAttachmentKind.Simulation, FileName = "sim-report.pdf", ContentBase64 = Convert.ToBase64String(new byte[] { 37, 80, 68, 70 }) }, default)).Success);
            Assert.IsFalse((await cam.ImportToolsAsync(new ImportCamToolsDto { RevisionId = 11, Csv = "T;D\n1;-4" }, default)).Success);
            var done = await cam.ImportToolsAsync(new ImportCamToolsDto { RevisionId = 11, Csv = "T;Name;D;L\n1;EM10;10;70\n2;DR6;6;35" }, default);
            Assert.IsTrue(done.Success, done.Message);
            Assert.AreEqual(0, done.Data.Missing.Count, string.Join(" | ", done.Data.Missing));
            Assert.AreEqual(2, done.Data.Tools.Count);
            Assert.IsNotNull(done.Data.SimulationAttachmentId);

            // a new version of the post retires the old one; the revision now points at a retired post
            var v2 = await cam.CreatePostAsync(new SavePostProcessorDto { Code = "HH-TNC640", Title = "پست هایدن‌هاین ۵ محور", CamSystem = "PowerMill", ControllerFamily = MfNcControllerFamily.HeidenhainConversational, FileName = "tnc640.pmoptz", ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("post body 2")) }, default);
            Assert.AreEqual(2, v2.Data.Version);
            v2 = await cam.PostActionAsync(new PostProcessorActionDto { Id = v2.Data.Id, Action = "submit", RowVersion = v2.Data.RowVersion }, default);
            rig.NewRequest(2, "PRODUCTIONMANAGER");
            cam = rig.Scope.ServiceProvider.GetRequiredService<MfCamEvidenceService>();
            Assert.IsTrue((await cam.PostActionAsync(new PostProcessorActionDto { Id = v2.Data.Id, Action = "approve", RowVersion = v2.Data.RowVersion }, default)).Success);
            rig.NewRequest(1, "ENGINEERING");
            var missing = (await rig.Scope.ServiceProvider.GetRequiredService<MfCamEvidenceService>().GetRevisionAsync(11, default)).Data.Missing;
            StringAssert.Contains(missing.Single(), "جاری نیست");
        }
    }
}
