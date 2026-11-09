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

            /// <summary>The records of what ran (one per program of the step with an approved revision), to be added in the same save as the booking (the caller fills the employee, the time and the user).</summary>
            public List<MfNcRun> Runs { get; init; } = new List<MfNcRun>();
        }

        private static readonly Result Pass = new Result();

        /// <summary>
        /// Every active program of the routing step that fits the machine (bound to it, to its work centre or to neither, same controller family): a step may need more than one program (two clampings).
        /// Order: programs with an approved revision first (review finding 3), then bound to this machine, then to the work centre; ties by id.
        /// </summary>
        public static async Task<List<MfNcProgram>> ProgramsForAsync(IUnitOfWork uow, MfOperation op, int machineId, CancellationToken ct)
        {
            if (op?.OperationTemplateId == null) return new List<MfNcProgram>();
            int templateId = op.OperationTemplateId.Value;
            List<MfNcProgram> candidates = await uow.Set<MfNcProgram>().AsNoTracking()
                .Where(p => !p.Deleted && p.Status == MfNcProgramStatus.Active && p.OperationTemplateId == templateId
                    && (p.MachineId == null || p.MachineId == machineId) && (p.WorkCenterId == null || op.WorkCenterId == null || p.WorkCenterId == op.WorkCenterId))
                .ToListAsync(ct);
            MfMachineConnection connection = await uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(c => c.MachineId == machineId && !c.Deleted, ct);
            MfNcControllerFamily family = connection?.ControllerFamily ?? MfNcControllerFamily.Unknown;
            return candidates.Where(p => DncText.Fits(p.ControllerFamily, family))
                .OrderBy(p => p.CurrentRevisionId.HasValue ? 0 : 1).ThenBy(p => p.MachineId == machineId ? 0 : p.WorkCenterId != null ? 1 : 2).ThenBy(p => p.Id).ToList();
        }

        /// <summary>The program the terminal shows first for the operation on the machine (see <see cref="ProgramsForAsync"/>).</summary>
        public static async Task<MfNcProgram> ProgramForAsync(IUnitOfWork uow, MfOperation op, int machineId, CancellationToken ct) =>
            (await ProgramsForAsync(uow, op, machineId, ct)).FirstOrDefault();

        /// <summary>
        /// May the setup, run or rework start? On a machine that requires the confirmed program every program of the step must have an approved revision, sent to this machine and confirmed on the control
        /// after the approval, and nothing else may have been written over that file on the machine since (another revision, another program with the same name, a late retry: review finding 2).
        /// </summary>
        public static async Task<Result> CheckStartAsync(IUnitOfWork uow, MfOperation op, int? machineId, MfBookingKind kind, CancellationToken ct)
        {
            if (!machineId.HasValue || op == null || !MfNcRules.Machining(kind)) return Pass;
            int machine = machineId.Value;
            List<MfNcProgram> programs = await ProgramsForAsync(uow, op, machine, ct);
            if (programs.Count == 0) return Pass;
            MfMachineConnection connection = await uow.Set<MfMachineConnection>().AsNoTracking().FirstOrDefaultAsync(c => c.MachineId == machine && !c.Deleted, ct);
            bool requires = connection != null && connection.IsActive && connection.RequireConfirmedProgram;
            if (requires)
            {
                MfNcProgram unapproved = programs.FirstOrDefault(p => !p.CurrentRevisionId.HasValue);
                if (unapproved != null)
                    return new Result { Error = $"برنامه {unapproved.Code} این مرحله هنوز نسخه تأییدشده ندارد؛ پیش از شروع باید تأیید، به این ماشین ارسال و روی کنترلر تأیید شود (DNC)." };
            }
            List<int> revisionIds = programs.Where(p => p.CurrentRevisionId.HasValue).Select(p => p.CurrentRevisionId.Value).ToList();
            if (revisionIds.Count == 0) return Pass;
            Dictionary<int, DateTimeOffset?> approvedAt = await uow.Set<MfNcRevision>().AsNoTracking().Where(r => revisionIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.ApprovedAt, ct);
            // every program written to this machine (on its way or there): a confirmation counts only while its file was not written over afterwards
            // only the files the current revisions were confirmed under matter (the query does not grow with the history of the machine)
            List<string> names = await uow.Set<MfNcTransfer>().AsNoTracking()
                .Where(t => !t.Deleted && t.MachineId == machine && t.Direction == MfNcTransferDirection.ToMachine && t.Status == MfNcTransferStatus.Confirmed && t.RevisionId != null && revisionIds.Contains(t.RevisionId.Value))
                .Select(t => t.TargetName).Distinct().ToListAsync(ct);
            List<MfDncRulesTransfer> written = names.Count == 0 ? new List<MfDncRulesTransfer>() : await uow.Set<MfNcTransfer>().AsNoTracking()
                .Where(t => !t.Deleted && t.MachineId == machine && t.Direction == MfNcTransferDirection.ToMachine && names.Contains(t.TargetName)
                    && (t.Status == MfNcTransferStatus.Sending || t.Status == MfNcTransferStatus.Delivered || t.Status == MfNcTransferStatus.Confirmed))
                .Select(t => new MfDncRulesTransfer { Id = t.Id, MachineId = t.MachineId, RevisionId = t.RevisionId, Status = t.Status, ConfirmedAt = t.ConfirmedAt, DeliveredAt = t.DeliveredAt, TargetName = t.TargetName })
                .ToListAsync(ct);
            var runs = new List<MfNcRun>();
            foreach (MfNcProgram program in programs.Where(p => p.CurrentRevisionId.HasValue))
            {
                int revisionId = program.CurrentRevisionId.Value;
                List<MfDncRulesTransfer> valid = written.Where(t => t.RevisionId == revisionId && t.Status == MfNcTransferStatus.Confirmed && !Overwritten(t, written)).ToList();
                var gate = MfNcRules.StartGate(requires, kind, program.Code, revisionId, approvedAt.TryGetValue(revisionId, out DateTimeOffset? at) ? at : null, machine,
                    valid.Select(t => new MfNcRules.GateTransfer { Id = t.Id, MachineId = t.MachineId, RevisionId = t.RevisionId, Status = t.Status, ConfirmedAt = t.ConfirmedAt }));
                if (gate.Error != null)
                {
                    bool replaced = written.Any(t => t.RevisionId == revisionId && t.Status == MfNcTransferStatus.Confirmed) && valid.Count == 0;
                    return new Result { Error = replaced ? $"پس از تأیید برنامه {program.Code} روی کنترلر، فایل دیگری روی همان نام در این ماشین نوشته شده است؛ نسخه جاری را دوباره ارسال و تأیید کنید (DNC)." : gate.Error };
                }
                // the run is recorded whether or not the machine requires the confirmation: the revision that is current is the one the operation runs by
                int? transferId = gate.Transfer?.Id ?? valid.OrderByDescending(t => t.ConfirmedAt).Select(t => (int?)t.Id).FirstOrDefault();
                runs.Add(new MfNcRun { OperationId = op.Id, MachineId = machine, ProgramId = program.Id, RevisionId = revisionId, TransferId = transferId, Kind = kind });
            }
            return new Result { Runs = runs };
        }

        /// <summary>True when another revision (or another program) went to the same file of the machine after this one was delivered, or is being written right now.</summary>
        private static bool Overwritten(MfDncRulesTransfer confirmed, List<MfDncRulesTransfer> written) =>
            written.Any(o => o.Id != confirmed.Id && o.RevisionId != confirmed.RevisionId && string.Equals(o.TargetName, confirmed.TargetName, StringComparison.OrdinalIgnoreCase)
                && (o.Status == MfNcTransferStatus.Sending || (o.DeliveredAt.HasValue && (!confirmed.DeliveredAt.HasValue || o.DeliveredAt.Value > confirmed.DeliveredAt.Value))));

        private sealed class MfDncRulesTransfer
        {
            public int Id { get; init; }
            public int MachineId { get; init; }
            public int? RevisionId { get; init; }
            public MfNcTransferStatus Status { get; init; }
            public DateTimeOffset? ConfirmedAt { get; init; }
            public DateTimeOffset? DeliveredAt { get; init; }
            public string TargetName { get; init; }
        }
    }
}
