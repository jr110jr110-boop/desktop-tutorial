using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Quality;

namespace Services.NasbNiro.Production
{
    /// <summary>
    /// R1p D12: welding on the shop floor. A welding step (<see cref="MfOperation.IsWelding"/>) starts only with the approved WPS of the step and only for a booking employee who holds a valid welder
    /// qualification for the welding process of that WPS (the same state rules the quality module applies to the weld log: not revoked, not superseded, not expired, welding continuity kept). The employee is
    /// tied to the qualified-person record of the quality module by the personnel file (<c>FnEmployee.QualifiedPersonId</c>, kept by HR), else by the user account both share. The quality module is read
    /// only here; the weld log that the finish drafts is a plain row of the existing table (visual test pending, to be completed by QC).
    /// </summary>
    internal static class MfWelding
    {
        public const string NoWps = "برای این مرحله جوشکاری، WPS در استاندارد مرحله تعیین نشده است؛ مهندسی باید WPS تأییدشده را به مرحله اختصاص دهد.";

        /// <summary>The approved WPS the welding step is welded by, or the Persian reason it is not available.</summary>
        public static async Task<(Wps Wps, string Error)> ApprovedWpsAsync(IUnitOfWork uow, MfOperation op, CancellationToken ct)
        {
            if (!op.WpsId.HasValue) return (null, NoWps);
            Wps wps = await uow.Set<Wps>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == op.WpsId.Value && !w.Deleted, ct);
            if (wps == null) return (null, "WPS تعیین‌شده برای این مرحله پیدا نشد.");
            if (wps.Status != WpsStatus.Approved)
                return (null, $"WPS شماره {wps.Number}/{wps.Revision} تأیید نهایی (کیفی و فنی) نشده است؛ جوشکاری شروع نمی‌شود.");
            return (wps, null);
        }

        /// <summary>The welder qualification of the employee that is usable on <paramref name="on"/> for the welding process of the WPS, or the Persian reason there is none.</summary>
        public static async Task<(Qualification Qualification, string Error)> WelderAsync(IUnitOfWork uow, FnEmployee employee, Wps wps, DateTime on, CancellationToken ct)
        {
            string who = MfNames.EmployeeName(employee);
            int? personId = employee.QualifiedPersonId;
            QualifiedPerson person = null;
            if (personId.HasValue) person = await uow.Set<QualifiedPerson>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId.Value && !p.Deleted, ct);
            if (person == null && employee.UserId.HasValue)
            {
                int userId = employee.UserId.Value;
                person = await uow.Set<QualifiedPerson>().AsNoTracking().Where(p => !p.Deleted && p.UserId == userId).OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
            }
            if (person == null)
                return (null, $"برای «{who}» پرونده صلاحیت جوشکار در ماژول کیفیت پیوند نشده است؛ منابع انسانی «پرونده صلاحیت» را در پرونده پرسنلی او تعیین کند.");
            if (!person.IsActive) return (null, $"پرونده صلاحیت جوشکار «{who}» غیرفعال است.");

            List<Qualification> candidates = await uow.Set<Qualification>().AsNoTracking()
                .Where(q => !q.Deleted && q.PersonId == person.Id && (q.Kind == QualificationKind.Welder || q.Kind == QualificationKind.WeldingOperator) && q.Method == wps.Process)
                .OrderByDescending(q => q.Id).ToListAsync(ct);
            if (candidates.Count == 0) return (null, $"«{who}» گواهی جوشکاری برای فرایند {wps.Process} (فرایند WPS) ندارد.");

            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            int months = (int)settings[QualitySettingKeys.WeldContinuityMonths], warn = (int)settings[QualitySettingKeys.ContinuityWarnDays];
            DateTime? last = await uow.Set<WeldLog>().AsNoTracking().Where(w => !w.Deleted && w.WeldDate <= on && w.Qualification.PersonId == person.Id && w.Qualification.Method == wps.Process)
                .MaxAsync(w => (DateTime?)w.WeldDate, ct);
            string reason = null;
            foreach (Qualification q in candidates)
            {
                QualificationState state = QualificationRules.State(q, on, QualificationRules.NeedsContinuity(q.Kind) ? last : null, months, warn);
                if (QualificationRules.Usable(state)) return (q, null);
                reason ??= $"گواهی جوشکاری «{who}» برای فرایند {wps.Process} معتبر نیست: {QualificationRules.StateText(state)}.";
            }
            return (null, reason);
        }

        /// <summary>
        /// Everything a welding step needs before somebody works on it: the approved WPS and, for the kinds of booking that weld (run and rework), a welder qualified for the process of that WPS.
        /// A setup, a wait or a cleaning booking welds nothing: only the approved WPS is required.
        /// </summary>
        public static async Task<(Wps Wps, Qualification Qualification, string Error)> CheckStartAsync(IUnitOfWork uow, MfOperation op, FnEmployee employee, MfBookingKind kind, DateTime on, CancellationToken ct)
        {
            (Wps wps, string error) = await ApprovedWpsAsync(uow, op, ct);
            if (error != null) return (null, null, error);
            if (kind != MfBookingKind.Run && kind != MfBookingKind.Rework) return (wps, null, null);
            (Qualification qualification, string problem) = await WelderAsync(uow, employee, wps, on, ct);
            return problem != null ? (wps, null, problem) : (wps, qualification, null);
        }

        /// <summary>
        /// The weld log drafted from the finished step: the work order, the item, the single planned serial (when the order has exactly one), the job card as the joint number, the WPS and the welder's
        /// qualification, visual test pending. QC completes it (filler batch, temperatures, visual result) in the quality module. Returns null with a note when it cannot be drafted.
        /// </summary>
        public static async Task<(WeldLog Log, string Note)> DraftWeldLogAsync(IUnitOfWork uow, int userId, MfProductionOrder order, MfOperation op, FnEmployee employee, CancellationToken ct)
        {
            (Wps wps, string error) = await ApprovedWpsAsync(uow, op, ct);
            if (error != null) return (null, error);
            (Qualification q, string problem) = await WelderAsync(uow, employee, wps, DateTime.Today, ct);
            if (problem != null) return (null, problem);
            string job = MfMapping.JobCard(order.Number, op);
            string[] serials = (order.SerialsPlanned ?? string.Empty).Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToArray();
            DateTimeOffset now = DateTimeOffset.Now;
            var log = new WeldLog
            {
                WeldDate = DateTime.Today, WorkOrderId = order.WorkOrderId, ItemId = order.ItemId, SerialNumber = serials.Length == 1 ? serials[0] : null, JointNo = job, WpsId = wps.Id, QualificationId = q.Id,
                Vt = VtResult.Pending, Note = $"پیش‌نویس از پایان عملیات جوشکاری {job} (دستور تولید {order.Number}) توسط {MfNames.EmployeeName(employee)}؛ بازرسی چشمی و مشخصات جوش را QC تکمیل کند.",
                CreateDate = now, ModifyDate = now, CreatorUserId = userId, ModifierUserId = userId,
            };
            uow.Set<WeldLog>().Add(log);
            return (log, null);
        }
    }
}
