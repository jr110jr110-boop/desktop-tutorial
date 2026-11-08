using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production.Dnc;
using ViewModels.NasbNiro.ProductionDtos;

namespace MsTests
{
    /// <summary>
    /// Production release P2 part B (live monitoring): the state from the stack-light inputs, the minutes per state in a window, the run share, the disagreement with the shop floor, the Modbus TCP client against a
    /// fake input module on the loopback, and the whole flow on the in-memory database (poll, alarm with a maintenance request, offline after three failed polls, the gateway push, the live board, the measured
    /// utilisation against the booked hours). Every expected figure is worked out by hand.
    /// </summary>
    [TestClass]
    public class MonitorTests
    {
        private static MfMachineMonitor Map(int? run = 0, int? alarm = 1, int? power = 2, int invert = 0) =>
            new MfMachineMonitor { Source = MfMonitorSource.ModbusInputs, RunInput = run, AlarmInput = alarm, PowerInput = power, InvertMask = invert };

        [TestMethod]
        public void The_Stack_Light_Gives_Alarm_First_Then_Running_Then_Idle_With_Power_Else_Off()
        {
            var m = Map();
            Assert.AreEqual(MfMachineState.Alarm, MfMonitorRules.FromInputs(new[] { true, true, true }, m), "red wins over green");
            Assert.AreEqual(MfMachineState.Running, MfMonitorRules.FromInputs(new[] { true, false, true }, m));
            Assert.AreEqual(MfMachineState.Idle, MfMonitorRules.FromInputs(new[] { false, false, true }, m));
            Assert.AreEqual(MfMachineState.Off, MfMonitorRules.FromInputs(new[] { false, false, false }, m));
            // without a power input a reachable machine that neither runs nor alarms is idle
            Assert.AreEqual(MfMachineState.Idle, MfMonitorRules.FromInputs(new[] { false, false }, Map(power: null)));
            // an active-low alarm relay (bit 1 inverted): a low input is an alarm
            Assert.AreEqual(MfMachineState.Alarm, MfMonitorRules.FromInputs(new[] { false, false, true }, Map(invert: 2)));
            Assert.AreEqual(MfMachineState.Idle, MfMonitorRules.FromInputs(new[] { false, true, true }, Map(invert: 2)));
            Assert.AreEqual(3, MfMonitorRules.InputCount(Map()), "inputs 0..2 are read");
            Assert.AreEqual(10, MfMonitorRules.Interval(Map()));
        }

        [TestMethod]
        public void Minutes_Per_State_Are_Cut_To_The_Window_And_The_Run_Share_Leaves_Offline_Out()
        {
            var from = new DateTime(2026, 11, 8, 8, 0, 0);
            var spans = new[]
            {
                new MfMonitorRules.Span { State = MfMachineState.Running, StartAt = from.AddHours(-1), EndAt = from.AddMinutes(90) },   // 90 min inside the window
                new MfMonitorRules.Span { State = MfMachineState.Idle, StartAt = from.AddMinutes(90), EndAt = from.AddMinutes(120) },   // 30
                new MfMonitorRules.Span { State = MfMachineState.Offline, StartAt = from.AddMinutes(120), EndAt = from.AddMinutes(180) }, // 60, not observed
                new MfMonitorRules.Span { State = MfMachineState.Running, StartAt = from.AddMinutes(180), EndAt = null },               // open: runs to now = +240 -> 60
            };
            var minutes = MfMonitorRules.Minutes(spans, from, from.AddHours(8), from.AddMinutes(240));
            Assert.AreEqual(150d, minutes[MfMachineState.Running]);
            Assert.AreEqual(30d, minutes[MfMachineState.Idle]);
            Assert.AreEqual(60d, minutes[MfMachineState.Offline]);
            Assert.AreEqual(83.3m, MfMonitorRules.RunPercent(minutes), "150 / (150 + 30)");
            Assert.IsNull(MfMonitorRules.RunPercent(MfMonitorRules.Minutes(Array.Empty<MfMonitorRules.Span>(), from, from.AddHours(1), from)));
        }

        [TestMethod]
        public void The_Machine_And_The_Shop_Floor_Disagree_Only_After_The_Set_Minutes()
        {
            Assert.AreEqual("booked-idle", MfMonitorRules.Mismatch(MfMachineState.Idle, 20, true, 15));
            Assert.AreEqual("booked-idle", MfMonitorRules.Mismatch(MfMachineState.Off, 15, true, 15));
            Assert.IsNull(MfMonitorRules.Mismatch(MfMachineState.Idle, 14.9, true, 15), "not long enough");
            Assert.AreEqual("running-unbooked", MfMonitorRules.Mismatch(MfMachineState.Running, 30, false, 15));
            Assert.IsNull(MfMonitorRules.Mismatch(MfMachineState.Running, 30, true, 15), "running with a booking: they agree");
            Assert.IsNull(MfMonitorRules.Mismatch(MfMachineState.Alarm, 30, true, 15), "an alarm has its own alert");
        }

        /// <summary>A Modbus TCP input module on the loopback answering function 2 with the given inputs.</summary>
        private static async Task<(TcpListener Listener, Task Server)> FakeModuleAsync(bool[] inputs)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Task server = Task.Run(async () =>
            {
                using TcpClient c = await listener.AcceptTcpClientAsync();
                NetworkStream s = c.GetStream();
                byte[] req = new byte[12];
                int read = 0;
                while (read < 12) read += await s.ReadAsync(req.AsMemory(read));
                int count = (req[10] << 8) | req[11];
                int bytes = (count + 7) / 8;
                var data = new byte[bytes];
                for (int i = 0; i < count && i < inputs.Length; i++) if (inputs[i]) data[i / 8] |= (byte)(1 << (i % 8));
                var resp = new List<byte> { req[0], req[1], 0, 0, 0, (byte)(3 + bytes), req[6], req[7], (byte)bytes };
                resp.AddRange(data);
                await s.WriteAsync(resp.ToArray());
            });
            await Task.Yield();
            return (listener, server);
        }

        [TestMethod]
        public async Task The_Modbus_Client_Reads_Ten_Discrete_Inputs_Bit_By_Bit()
        {
            bool[] wanted = { true, false, true, true, false, false, false, false, false, true };
            var (listener, server) = await FakeModuleAsync(wanted);
            try
            {
                var (inputs, error) = await new ModbusTcpReader().ReadAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, MfModbusFunction.DiscreteInputs, 0, 10, CancellationToken.None);
                Assert.IsNull(error, error);
                CollectionAssert.AreEqual(wanted, inputs);
                await server;
            }
            finally { listener.Stop(); }
            var (none, refused) = await new ModbusTcpReader().ReadAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, MfModbusFunction.DiscreteInputs, 0, 3, CancellationToken.None);
            Assert.IsNull(none);
            StringAssert.Contains(refused, "اتصال برقرار نشد");
        }

        // ------------------------------------------------------------------------------------------ the flow

        private sealed class FakeReader : IMfModbusReader
        {
            public bool[] Inputs { get; set; }
            public string Error { get; set; }
            public int Calls { get; private set; }
            public Task<(bool[] Inputs, string Error)> ReadAsync(string host, int port, byte unit, MfModbusFunction function, int start, int count, CancellationToken ct)
            {
                Calls++;
                return Task.FromResult(Error != null ? ((bool[])null, Error) : (Inputs, (string)null));
            }
        }

        private sealed class User : IInventoryUserContext
        {
            public int UserId { get; set; } = 1;
            public string Roles { get; set; } = "PRODUCTIONMANAGER";
            public bool IsInRole(string role) => (role ?? "").Split(',').Select(r => r.Trim()).Any(r => Roles.Split(',').Contains(r));
        }

        [TestMethod]
        public async Task Poll_Alarm_Offline_Gateway_Live_Board_And_Measured_Utilisation()
        {
            var reader = new FakeReader { Inputs = new[] { true, false, true } };
            var user = new User();
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory(s =>
            {
                s.AddScoped<IInventoryUserContext>(_ => user);
                s.AddSingleton<IMfModbusReader>(reader);
                s.AddScoped<MfMonitorService>();
            });
            DateTime t0 = new DateTime(2026, 11, 8, 8, 0, 0);
            DateTime now = t0;
            MfMonitorService Svc(IServiceScope sc) { var x = sc.ServiceProvider.GetRequiredService<MfMonitorService>(); x.Clock = () => now; return x; }

            using (IServiceScope sc = provider.CreateScope())
            {
                var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<Machine>().Add(new Machine { Id = 3, Title = "Mikron VCP 710", IsActive = true });
                db.Set<Machine>().Add(new Machine { Id = 2, Title = "Reiden", IsActive = true });
                db.Set<MfMachineConnection>().Add(new MfMachineConnection { Id = 2, MachineId = 2, LinkKind = MfNcLinkKind.EdgeGateway, IsActive = true, GatewayKeyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("box-key"))) });
                await db.SaveChangesAsync();
                var svc = Svc(sc);
                Assert.IsFalse((await svc.SaveMonitorAsync(new SaveMachineMonitorDto { MachineId = 3, Source = MfMonitorSource.ModbusInputs, Host = "10.0.0.50" }, default)).Success, "the run input is required");
                var saved = await svc.SaveMonitorAsync(new SaveMachineMonitorDto { MachineId = 3, Source = MfMonitorSource.ModbusInputs, Host = "10.0.0.50", RunInput = 0, AlarmInput = 1, PowerInput = 2, AlarmCreatesRequest = true, MismatchMinutes = 15 }, default);
                Assert.IsTrue(saved.Success, saved.Message);
                Assert.IsTrue((await svc.SaveMonitorAsync(new SaveMachineMonitorDto { MachineId = 2, Source = MfMonitorSource.GatewayPush, PollSeconds = 10 }, default)).Success);
            }

            // 08:00 running
            using (IServiceScope sc = provider.CreateScope())
            {
                var r = await Svc(sc).PollAsync(default);
                Assert.AreEqual(1, r.Polled);
                Assert.AreEqual(1, r.Changed);
            }
            // 08:00:05 polled again too early: skipped; 08:00:10 still running: no change
            now = t0.AddSeconds(5);
            using (IServiceScope sc = provider.CreateScope()) Assert.AreEqual(0, (await Svc(sc).PollAsync(default)).Polled);
            now = t0.AddSeconds(10);
            using (IServiceScope sc = provider.CreateScope()) { var r = await Svc(sc).PollAsync(default); Assert.AreEqual(1, r.Polled); Assert.AreEqual(0, r.Changed); }

            // 09:00 alarm: a maintenance request is opened and linked to the alarm period
            now = t0.AddHours(1);
            reader.Inputs = new[] { false, true, true };
            using (IServiceScope sc = provider.CreateScope())
            {
                var r = await Svc(sc).PollAsync(default);
                Assert.AreEqual(1, r.Alarms);
                Assert.AreEqual(1, r.RequestsOpened);
                var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                MfMaintenanceRequest req = await db.Set<MfMaintenanceRequest>().SingleAsync();
                Assert.AreEqual(3, req.MachineId);
                StringAssert.Contains(req.Description, "پایش خودکار");
                Assert.AreEqual(req.Id, (await db.Set<MfMachineStateEvent>().SingleAsync(e => e.State == MfMachineState.Alarm)).MaintenanceRequestId);
            }
            // 09:30 idle (cleared); 10:00 the module stops answering: offline only after the third failed poll
            now = t0.AddMinutes(90);
            reader.Inputs = new[] { false, false, true };
            using (IServiceScope sc = provider.CreateScope()) await Svc(sc).PollAsync(default);
            reader.Error = "اتصال برقرار نشد (TimedOut).";
            for (int i = 0; i < 3; i++)
            {
                now = t0.AddHours(2).AddSeconds(10 * i);
                using IServiceScope sc = provider.CreateScope();
                var r = await Svc(sc).PollAsync(default);
                Assert.AreEqual(i == 2 ? 1 : 0, r.Offline, "poll " + (i + 1));
            }
            // 10:30 the module answers again: running
            now = t0.AddMinutes(150);
            reader.Error = null;
            reader.Inputs = new[] { true, false, true };
            using (IServiceScope sc = provider.CreateScope()) await Svc(sc).PollAsync(default);

            // the edge box of the Reiden pushes its state with the machine key; a wrong key is refused
            now = t0.AddMinutes(155);
            using (IServiceScope sc = provider.CreateScope())
            {
                var svc = Svc(sc);
                Assert.AreEqual(HttpStatusCode.Unauthorized, (await svc.GatewayStatusAsync("2", "wrong", "10.0.0.9", new GatewayStatusDto { State = MfMachineState.Running }, default)).HttpStatusCode);
                var pushed = await svc.GatewayStatusAsync("2", "box-key", "10.0.0.9", new GatewayStatusDto { State = MfMachineState.Running, ProgramName = "100.H", SpindleRpm = 4500, PartCount = 17 }, default);
                Assert.IsTrue(pushed.Success, pushed.Message);
            }

            // 11:00 the live board (the operator of the Mikron has no open booking: it runs unbooked for 30 minutes, above 15)
            now = t0.AddHours(3);
            using (IServiceScope sc = provider.CreateScope())
            {
                var board = (await Svc(sc).GetLiveAsync(null, default)).Data;
                Assert.AreEqual(2, board.Machines.Count);
                Assert.AreEqual(2, board.Running);
                LiveMachineDto mikron = board.Machines.Single(x => x.MachineId == 3);
                Assert.AreEqual(MfMachineState.Running, mikron.State);
                Assert.AreEqual(30, mikron.MinutesInState);
                Assert.AreEqual("running-unbooked", mikron.Mismatch);
                // today 08:00-11:00: running 08:00-09:00 + 10:30-11:00 = 90, alarm 09:00-09:30 = 30, idle 09:30-10:00:20 = 30.3, offline 10:00:20-10:30 = 29.7
                Assert.AreEqual(90d, mikron.Today.Running);
                Assert.AreEqual(30d, mikron.Today.Alarm);
                Assert.AreEqual(30.3d, mikron.Today.Idle);
                Assert.AreEqual(29.7d, mikron.Today.Offline);
                Assert.AreEqual(59.9m, mikron.TodayRunPercent, "90 / (90 + 30 + 30.33) = 59.87");
                LiveMachineDto reiden = board.Machines.Single(x => x.MachineId == 2);
                Assert.AreEqual("100.H", reiden.ProgramName);
                Assert.AreEqual(4500, reiden.SpindleRpm);
                Assert.AreEqual(17L, reiden.PartCount);
            }

            // the Reiden box went silent for more than three intervals: offline
            now = t0.AddHours(3).AddSeconds(31);
            using (IServiceScope sc = provider.CreateScope()) Assert.AreEqual(1, (await Svc(sc).PollAsync(default)).Offline);
        }

        [TestMethod]
        public async Task Measured_Running_Hours_Are_Compared_With_The_Hours_Booked_On_The_Machine()
        {
            var user = new User();
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory(s =>
            {
                s.AddScoped<IInventoryUserContext>(_ => user);
                s.AddSingleton<IMfModbusReader>(new FakeReader());
                s.AddScoped<MfMonitorService>();
            });
            DateTime d = new DateTime(2026, 11, 7);
            using IServiceScope sc = provider.CreateScope();
            var db = sc.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Set<Machine>().Add(new Machine { Id = 3, Title = "Mikron VCP 710", IsActive = true });
            db.Set<MfMachineMonitor>().Add(new MfMachineMonitor { Id = 1, MachineId = 3, Source = MfMonitorSource.ModbusInputs, RunInput = 0, IsActive = true });
            // measured: running 08:00-12:00 (4 h), idle 12:00-13:00, running 13:00-15:00 (2 h): 6 h running
            db.Set<MfMachineStateEvent>().AddRange(
                new MfMachineStateEvent { MachineId = 3, State = MfMachineState.Running, StartAt = d.AddHours(8), EndAt = d.AddHours(12) },
                new MfMachineStateEvent { MachineId = 3, State = MfMachineState.Idle, StartAt = d.AddHours(12), EndAt = d.AddHours(13) },
                new MfMachineStateEvent { MachineId = 3, State = MfMachineState.Running, StartAt = d.AddHours(13), EndAt = d.AddHours(15) },
                new MfMachineStateEvent { MachineId = 3, State = MfMachineState.Alarm, StartAt = d.AddHours(15), EndAt = d.AddHours(15.5) });
            // booked: run 08:00-16:00 (8 h) and a waiting booking that does not count
            db.Set<MfTimeBooking>().AddRange(
                new MfTimeBooking { OperationId = 1, EmployeeId = 1, MachineId = 3, Kind = MfBookingKind.Run, StartAt = d.AddHours(8), EndAt = d.AddHours(16) },
                new MfTimeBooking { OperationId = 1, EmployeeId = 1, MachineId = 3, Kind = MfBookingKind.Waiting, StartAt = d.AddHours(16), EndAt = d.AddHours(17) });
            await db.SaveChangesAsync();
            var svc = sc.ServiceProvider.GetRequiredService<MfMonitorService>();
            svc.Clock = () => d.AddDays(1);
            var u = (await svc.GetUtilisationAsync(new UtilisationQuery { From = d, To = d.AddDays(1) }, default)).Data.Single();
            Assert.AreEqual(6m, u.MeasuredRunHours);
            Assert.AreEqual(8m, u.BookedHours);
            Assert.AreEqual(33.3m, u.BookingGapPercent, "(8 - 6) / 6: a third more booked than the machine ran");
            Assert.AreEqual(1, u.Alarms);
            Assert.AreEqual(80m, u.RunPercent, "360 running of 450 observed minutes");
            Assert.IsFalse((await svc.GetUtilisationAsync(new UtilisationQuery { From = d, To = d }, default)).Success);
        }
    }
}
