using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Office;

namespace Services.NasbNiro.Production.Dnc
{
    /// <summary>Role names of the DNC (production release P2 part A). ★ provisional decisions, to be confirmed by the company.</summary>
    public static class DncRoles
    {
        /// <summary>Everybody who reads the production master data, the operators included (they see the program of their operation).</summary>
        public const string Readers = ProductionRoles.MasterReaders;

        /// <summary>The CAM programmers: open programs and upload revisions.</summary>
        public const string Writers = "PRODUCTIONMANAGER,ENGINEERING,ENGINEER,PMO";

        /// <summary>Approve or reject a revision (never its author, never an AI seat).</summary>
        public const string Approvers = "PRODUCTIONMANAGER,SHOPSUPERVISOR,ENGINEERING,ENGINEER";

        /// <summary>Send a program to a machine and confirm it on the control.</summary>
        public const string Senders = ProductionRoles.Terminal;

        /// <summary>Fetch a program back from a machine (an edited program at the control).</summary>
        public const string Receivers = "SHOPSUPERVISOR,PRODUCTIONMANAGER,ENGINEERING,ENGINEER";

        /// <summary>Machine links: settings and the gateway key.</summary>
        public const string LinkEditors = ProductionRoles.MasterEditors;

        /// <summary>Test the link of a machine.</summary>
        public const string LinkTesters = ProductionRoles.MasterEditors + ",MAINTENANCE,SHOPSUPERVISOR";
    }

    internal static class DncText
    {
        public static string Family(MfNcControllerFamily f) => f switch
        {
            MfNcControllerFamily.Unknown => "نامشخص",
            MfNcControllerFamily.Fanuc => "فانوک",
            MfNcControllerFamily.Siemens => "زیمنس",
            MfNcControllerFamily.HeidenhainConversational => "هایدن‌هاین (گفت‌وگویی)",
            MfNcControllerFamily.HeidenhainIso => "هایدن‌هاین (ISO)",
            MfNcControllerFamily.Mitsubishi => "میتسوبیشی",
            MfNcControllerFamily.Haas => "هاس",
            MfNcControllerFamily.Mazak => "مزاک",
            MfNcControllerFamily.Okuma => "اوکوما",
            MfNcControllerFamily.Fagor => "فاگور",
            MfNcControllerFamily.Philips => "فیلیپس / میل‌پلاس",
            _ => "سایر",
        };

        public static string TransferStatus(MfNcTransferStatus s) => s switch
        {
            MfNcTransferStatus.Queued => "در صف",
            MfNcTransferStatus.Sending => "در حال انتقال",
            MfNcTransferStatus.Delivered => "تحویل به کنترلر",
            MfNcTransferStatus.Confirmed => "تأیید اپراتور",
            MfNcTransferStatus.Failed => "خطا",
            MfNcTransferStatus.Cancelled => "لغو شده",
            MfNcTransferStatus.Received => "دریافت‌شده",
            _ => s.ToString(),
        };

        public static string RevisionStatus(MfNcRevisionStatus s) => s switch
        {
            MfNcRevisionStatus.Draft => "پیش‌نویس",
            MfNcRevisionStatus.Submitted => "منتظر تأیید",
            MfNcRevisionStatus.Approved => "تأییدشده (جاری)",
            MfNcRevisionStatus.Superseded => "جایگزین‌شده",
            MfNcRevisionStatus.Rejected => "ردشده",
            MfNcRevisionStatus.Withdrawn => "پس‌گرفته",
            _ => s.ToString(),
        };

        /// <summary>A program family and a machine family fit when either is unknown or they are equal.</summary>
        public static bool Fits(MfNcControllerFamily program, MfNcControllerFamily machine) =>
            program == MfNcControllerFamily.Unknown || machine == MfNcControllerFamily.Unknown || program == machine;
    }

    /// <summary>Inbox items of the DNC gates.</summary>
    internal static class DncGates
    {
        public static Task RevisionAsync(INotificationService notify, MfNcRevision revision, string programCode, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "mf:nc-rev:" + revision.Id, revision.Status == MfNcRevisionStatus.Submitted && !revision.Deleted, "production", "nc-revision", revision.Id,
                $"نسخه {revision.Revision} برنامه CNC {programCode} منتظر تأیید است", "/production/dnc/programs/" + revision.ProgramId, DncRoles.Approvers, ct);

        public static Task TransferFailedAsync(INotificationService notify, MfNcTransfer transfer, string machineTitle, CancellationToken ct) =>
            OfficeGates.SyncAsync(notify, "mf:nc-xfer:" + transfer.Id, transfer.Status == MfNcTransferStatus.Failed && transfer.Attempts >= MfDncService.MaxAttempts && !transfer.Deleted, "production", "nc-transfer", transfer.Id,
                $"انتقال برنامه به ماشین {machineTitle} ناموفق ماند", "/production/dnc/transfers?machineId=" + transfer.MachineId, ProductionRoles.Supervisors, ct, kind: Entities.Model.NasbNiro.Office.OfInboxKind.Task);
    }

    /// <summary>The DNC check at the start of an operation on the terminal (called by MfShopFloorService.StartAsync).</summary>
    public static class MfDncGate
    {
        public sealed class Result
        {
            public string Error { get; init; }

            /// <summary>The record of what ran, to be added in the same save as the booking (the caller fills the employee, the time and the user).</summary>
            public MfNcRun Run { get; init; }
        }

        private static readonly Result Pass = new Result();

        /// <summary>The program the terminal uses for the operation on the machine: an active program of the routing step, the one bound to this machine first, then to the work centre, then any; ties by id.</summary>
        public static async Task<MfNcProgram> ProgramForAsync(IUnitOfWork uow, MfOperation op, int machineId, CancellationToken ct)
        {
            if (op?.OperationTemplateId == null) return null;
            int templateId = op.OperationTemplateId.Value;
            List<MfNcProgram> candidates = await uow.Set<MfNcProgram>().AsNoTracking()
                .Where(p => !p.Deleted && p.Status == MfNcProgramStatus.Active && p.OperationTemplateId == templateId
                    && (p.MachineId == null || p.MachineId == machineId) && (p.WorkCenterId == null || op.WorkCenterId == null || p.WorkCenterId == op.WorkCenterId))
                .ToListAsync(ct);
            MfMachineConnection connection = await uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(c => c.MachineId == machineId && !c.Deleted, ct);
            MfNcControllerFamily family = connection?.ControllerFamily ?? MfNcControllerFamily.Unknown;
            return candidates.Where(p => DncText.Fits(p.ControllerFamily, family))
                .OrderBy(p => p.MachineId == machineId ? 0 : p.WorkCenterId != null ? 1 : 2).ThenBy(p => p.Id).FirstOrDefault();
        }

        public static async Task<Result> CheckStartAsync(IUnitOfWork uow, MfOperation op, int? machineId, MfBookingKind kind, CancellationToken ct)
        {
            if (!machineId.HasValue || op == null || (kind != MfBookingKind.Run && kind != MfBookingKind.Setup)) return Pass;
            MfNcProgram program = await ProgramForAsync(uow, op, machineId.Value, ct);
            if (program?.CurrentRevisionId == null) return Pass;
            MfMachineConnection connection = await uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(c => c.MachineId == machineId.Value && !c.Deleted, ct);
            bool requires = connection != null && connection.IsActive && connection.RequireConfirmedProgram;
            MfNcRevision current = await uow.Set<MfNcRevision>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == program.CurrentRevisionId.Value, ct);
            int revisionId = program.CurrentRevisionId.Value, machine = machineId.Value;
            List<MfDncRulesTransfer> transfers = await uow.Set<MfNcTransfer>().AsNoTracking()
                .Where(t => !t.Deleted && t.MachineId == machine && t.RevisionId == revisionId && t.Direction == MfNcTransferDirection.ToMachine && t.Status == MfNcTransferStatus.Confirmed)
                .Select(t => new MfDncRulesTransfer { Id = t.Id, MachineId = t.MachineId, RevisionId = t.RevisionId, Status = t.Status, ConfirmedAt = t.ConfirmedAt })
                .ToListAsync(ct);
            var gate = MfNcRules.StartGate(requires, kind, program.Code, revisionId, current?.ApprovedAt, machine,
                transfers.Select(t => new MfNcRules.GateTransfer { Id = t.Id, MachineId = t.MachineId, RevisionId = t.RevisionId, Status = t.Status, ConfirmedAt = t.ConfirmedAt }));
            if (gate.Error != null) return new Result { Error = gate.Error };
            // the run is recorded whether or not the machine requires the confirmation: the revision that is current is the one the operation runs by
            int? transferId = gate.Transfer?.Id ?? transfers.OrderByDescending(t => t.ConfirmedAt).Select(t => (int?)t.Id).FirstOrDefault();
            return new Result { Run = new MfNcRun { OperationId = op.Id, MachineId = machine, ProgramId = program.Id, RevisionId = revisionId, TransferId = transferId, Kind = kind } };
        }

        private sealed class MfDncRulesTransfer
        {
            public int Id { get; init; }
            public int MachineId { get; init; }
            public int? RevisionId { get; init; }
            public MfNcTransferStatus Status { get; init; }
            public DateTimeOffset? ConfirmedAt { get; init; }
        }
    }
}
