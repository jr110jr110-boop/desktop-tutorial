using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Production;

namespace Services.NasbNiro.Quality
{
    /// <summary>
    /// Package C (H7): the one gate for special process steps. A step flagged as a special process (heat treatment, nitriding, coating ...) is finished only with a run that passed, that a second person (QA) reviewed,
    /// that was done under an approved qualification valid on the day, by a qualified operator, on a machine with a valid survey and with calibrated instruments. A subcontracted step needs a supplier approved for the
    /// process and the evidence of the supplier (certificate and chart). One gate, one Persian message: the MES finish, the QC decision, the completion of an outside step and the legacy part operation all call it.
    /// </summary>
    internal static class SpecialProcessGate
    {
        public static bool On(Dictionary<string, decimal> settings, string key) => settings != null && settings.TryGetValue(key, out decimal v) && v >= 1;

        private static string Day(DateTime d) => FiscalCalendars.Text(d);

        // ================================================================== people

        /// <summary>The competence record (qualified person) of an employee: by the personnel file, else by the user account both share (the rule the welding gate uses).</summary>
        public static async Task<QualifiedPerson> PersonOfAsync(IUnitOfWork uow, FnEmployee employee, CancellationToken ct)
        {
            if (employee == null) return null;
            QualifiedPerson person = null;
            if (employee.QualifiedPersonId.HasValue)
                person = await uow.Set<QualifiedPerson>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == employee.QualifiedPersonId.Value && !p.Deleted, ct);
            if (person == null && employee.UserId.HasValue)
            {
                int userId = employee.UserId.Value;
                person = await uow.Set<QualifiedPerson>().AsNoTracking().Where(p => !p.Deleted && p.UserId == userId).OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
            }
            return person;
        }

        /// <summary>Null when the person holds a valid operator competence record for the process on the day; otherwise the Persian reason.</summary>
        public static async Task<string> CompetenceProblemAsync(IUnitOfWork uow, QualifiedPerson person, SpProcessType type, DateTime on, string who, CancellationToken ct)
        {
            if (person == null) return $"برای «{who}» پرونده صلاحیت در ماژول کیفیت پیوند نشده است؛ منابع انسانی «پرونده صلاحیت» را در پرونده پرسنلی او تعیین کند.";
            if (!person.IsActive) return $"پرونده صلاحیت «{who}» غیرفعال است.";
            string code = type.Code;
            List<Qualification> held = await uow.Set<Qualification>().AsNoTracking()
                .Where(q => !q.Deleted && q.PersonId == person.Id && q.Kind == QualificationKind.SpecialProcessOperator && q.Method == code).ToListAsync(ct);
            return QualificationRules.CheckHeld(held, on, 6, $"اپراتور {type.Title} ({who})");
        }

        // ================================================================== qualification

        /// <summary>
        /// Null when the qualification was approved (it may have been superseded by a renewal since) and its period covers the whole run. Fix round 1 (M1): a run is judged by the state AT THE RUN, so a renewal does not
        /// undo the runs made under the old qualification; only a voided, rejected or unfinished record fails.
        /// </summary>
        public static string QualificationProblem(SpQualification q, DateTime from, DateTime to)
        {
            if (q == null) return "صلاحیت فرایند پیدا نشد.";
            if (q.Status != SpQualStatus.Approved && q.Status != SpQualStatus.Superseded)
                return $"صلاحیت فرایند {q.Number} در وضعیت «تاییدشده» نیست.";
            if (!SpecialProcessRules.Covers(q.ValidFrom, q.ValidTo, from) || !SpecialProcessRules.Covers(q.ValidFrom, q.ValidTo, to))
                return $"صلاحیت فرایند {q.Number} در تاریخ اجرا ({Day(from)}) معتبر نیست (اعتبار: {Day(q.ValidFrom)} تا {Day(q.ValidTo)}).";
            return null;
        }

        /// <summary>The procedure revision of the qualification is still the one in force in the EDMS.</summary>
        public static async Task<bool> ProcedureInForceAsync(IUnitOfWork uow, SpQualification q, CancellationToken ct)
        {
            PcDocStatus? status = await uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => r.Id == q.ProcedureRevisionId && r.DeliverableId == q.ProcedureDeliverableId && !r.Deleted).Select(r => (PcDocStatus?)r.Status).FirstOrDefaultAsync(ct);
            return status == PcDocStatus.Released || status == PcDocStatus.AsBuilt;
        }

        /// <summary>The machine (survey) and the instruments (calibration) of a run against the qualification. Null = fine.</summary>
        public static async Task<string> EquipmentProblemAsync(IUnitOfWork uow, SpQualification q, SpProcessType type, int? machineId, List<int> instrumentIds, DateTime runStart, CancellationToken ct)
        {
            if (type.RequiresMachine)
            {
                if (!machineId.HasValue) return $"دستگاه (کوره/خط) اجرای {type.Title} مشخص نشده است.";
                SpQualEquipment row = q.Equipments.FirstOrDefault(e => e.Kind == SpEquipmentKind.Machine && e.MachineId == machineId.Value);
                string title = await uow.Set<Machine>().AsNoTracking().Where(m => m.Id == machineId.Value).Select(m => m.Title).FirstOrDefaultAsync(ct) ?? ("#" + machineId.Value);
                if (row == null) return $"دستگاه «{title}» جزو تجهیزات صلاحیت فرایند {q.Number} نیست.";
                if (type.RequiresSurvey)
                {
                    string survey = SpecialProcessRules.SurveyProblem(row, runStart, title);
                    if (survey != null) return survey;
                }
            }
            List<int> ids = (instrumentIds ?? new List<int>()).Distinct().ToList();
            if (type.RequiredInstrumentClass.HasValue)
            {
                if (ids.Count == 0) return $"برای {type.Title} دست‌کم یک ابزار اندازه‌گیری (ترموکوپل/سنجه) با کالیبراسیون معتبر لازم است.";
                List<EquipmentType> classes = await uow.Set<MeasuringEquipment>().AsNoTracking().Where(e => ids.Contains(e.Id) && !e.Deleted).Select(e => e.Type).ToListAsync(ct);
                if (!classes.Contains(type.RequiredInstrumentClass.Value)) return $"هیچ‌یک از ابزارهای اجرا از رده لازم ({type.RequiredInstrumentClass.Value}) نیست.";
            }
            foreach (int id in ids)
                if (!q.Equipments.Any(e => e.Kind == SpEquipmentKind.Instrument && e.EquipmentId == id))
                {
                    string code = await uow.Set<MeasuringEquipment>().AsNoTracking().Where(e => e.Id == id).Select(e => e.Code).FirstOrDefaultAsync(ct) ?? ("#" + id);
                    return $"ابزار {code} جزو تجهیزات صلاحیت فرایند {q.Number} نیست.";
                }
            if (ids.Count > 0)
            {
                string calibration = await QualityChecks.EquipmentOnDayAsync(uow, ids, runStart.Date, ct);   // the calibration valid on the day of the run, not today (M1)
                if (calibration != null) return calibration;
            }
            return null;
        }

        // ================================================================== supplier

        /// <summary>Null when the supplier holds an approved scope for the process that covers the day and is not blocked in the approved vendor list.</summary>
        public static async Task<string> SupplierProblemAsync(IUnitOfWork uow, int? providerId, SpProcessType type, DateTime on, CancellationToken ct)
        {
            if (!providerId.HasValue) return $"پیمانکار مرحله {type.Title} مشخص نیست.";
            string name = await uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == providerId.Value).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? ("#" + providerId.Value);
            bool blocked = await uow.Set<SupplierProfile>().AsNoTracking().AnyAsync(p => p.ProviderId == providerId.Value && p.Blocked, ct);
            if (blocked) return $"پیمانکار «{name}» در فهرست تامین‌کنندگان مجاز مسدود است.";
            int typeId = type.Id;
            List<SpSupplierApproval> rows = await uow.Set<SpSupplierApproval>().AsNoTracking().Where(a => !a.Deleted && a.ProviderId == providerId.Value && a.TypeId == typeId && (a.Status == SpApprovalStatus.Approved || a.Status == SpApprovalStatus.Superseded)).ToListAsync(ct);
            if (rows.Count == 0) return $"پیمانکار «{name}» برای فرایند «{type.Title}» تایید نشده است؛ دامنه تایید تامین‌کننده را QA ثبت و نفر دوم تایید کند.";
            if (!rows.Any(a => SpecialProcessRules.Covers(a.ValidFrom, a.ValidTo, on)))
                return $"تایید پیمانکار «{name}» برای فرایند «{type.Title}» در تاریخ {Day(on)} معتبر نیست.";
            return null;
        }

        // ================================================================== runs of one operation

        /// <summary>The run of an operation must be on the evidence list: a subcontract run needs both files and the certificate number.</summary>
        private static string EvidenceProblem(SpRun run, Dictionary<string, decimal> settings)
        {
            if (!On(settings, QualitySettingKeys.PkcSpSubcontractEvidence)) return null;
            if (!run.ChartDocumentId.HasValue) return $"اجرای {run.Number}: نمودار/سابقه فرایند پیمانکار پیوست نشده است.";
            if (!run.CertificateDocumentId.HasValue || string.IsNullOrWhiteSpace(run.CertificateNumber)) return $"اجرای {run.Number}: گواهی پیمانکار (فایل و شماره) ثبت نشده است.";
            return null;
        }

        /// <summary>
        /// The answer for one flagged step: null = the step may finish, otherwise the Persian reason. <paramref name="required"/> is the quantity the step reports; <paramref name="scrap"/> the part of it that is scrapped
        /// (it needs no passed run). <paramref name="bookingEmployees"/> are the employees with run bookings on the step (each must be qualified for the process).
        /// </summary>
        public static async Task<string> FinishProblemAsync(IUnitOfWork uow, MfProductionOrder order, MfOperation op, decimal required, decimal scrap, CancellationToken ct)
        {
            if (op == null || !op.IsSpecialProcess || !op.SpecialProcessTypeId.HasValue) return null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            if (!On(settings, QualitySettingKeys.PkcSpEnforce)) return null;
            SpProcessType type = await uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == op.SpecialProcessTypeId.Value, ct);
            if (type == null) return "نوع فرایند ویژه این گام پیدا نشد؛ مهندسی استاندارد عملیات را اصلاح کند.";
            if (type.ExistingControl) return null;
            string job = MfNames.Operation(order.Number, op.Sequence);
            string head = $"گام {job} ({op.Title}) فرایند ویژه «{type.Title}» است: ";

            List<SpRun> runs = await uow.Set<SpRun>().AsNoTracking().Include(r => r.Values).Include(r => r.Instruments)
                .Where(r => !r.Deleted && r.OperationId == op.Id && r.Status == SpRunStatus.Recorded).OrderBy(r => r.Id).ToListAsync(ct);
            return await JudgeRunsAsync(uow, runs, type, op.IsOutside, op.OutsideSupplierId, required, scrap, settings, head, op.Id, null, ct);
        }

        /// <summary>The same gate for a legacy part operation (a serial or lot stage of the traceability module) whose template is flagged as a special process.</summary>
        public static async Task<string> PartOperationProblemAsync(IUnitOfWork uow, PartOperation op, CancellationToken ct)
        {
            if (op == null || !op.OperationTemplateId.HasValue) return null;
            int templateId = op.OperationTemplateId.Value;
            MfOperationStandard std = await uow.Set<MfOperationStandard>().AsNoTracking().FirstOrDefaultAsync(s => s.OperationTemplateId == templateId && !s.Deleted, ct);
            if (std == null || !std.IsSpecialProcess || !std.SpecialProcessTypeId.HasValue) return null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            if (!On(settings, QualitySettingKeys.PkcSpEnforce)) return null;
            SpProcessType type = await uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == std.SpecialProcessTypeId.Value, ct);
            if (type == null) return "نوع فرایند ویژه این مرحله پیدا نشد.";
            if (type.ExistingControl) return null;
            string head = $"مرحله «{op.Title}» فرایند ویژه «{type.Title}» است: ";
            List<SpRun> runs = await uow.Set<SpRun>().AsNoTracking().Include(r => r.Values).Include(r => r.Instruments)
                .Where(r => !r.Deleted && r.PartOperationId == op.Id && r.Status == SpRunStatus.Recorded).OrderBy(r => r.Id).ToListAsync(ct);
            return await JudgeRunsAsync(uow, runs, type, std.IsOutside, std.DefaultSupplierId, 0m, 0m, settings, head, null, op.Id, ct);
        }

        private static async Task<string> JudgeRunsAsync(IUnitOfWork uow, List<SpRun> runs, SpProcessType type, bool outside, int? supplierId, decimal required, decimal scrap,
            Dictionary<string, decimal> settings, string head, int? operationId, int? partOperationId, CancellationToken ct)
        {
            if (runs.Count == 0) return head + "بدون ثبت اجرای فرایند (Run) پایان نمی‌یابد؛ اجرا، پارامترها و نمودار ثبت و QA بازبینی کند.";

            // a failed run must have gone through its NCR: the report is closed, or a valid concession covers the pieces
            decimal conceded = 0m;
            foreach (SpRun failed in runs.Where(r => r.Result == SpRunResult.Fail))
            {
                NonConformance ncr = failed.NcrId.HasValue ? await uow.Set<NonConformance>().AsNoTracking().FirstOrDefaultAsync(n => n.Id == failed.NcrId.Value && !n.Deleted, ct) : null;
                if (ncr == null) return head + $"اجرای {failed.Number} مردود شده و گزارش عدم انطباق ندارد.";
                if (ConcessionRules.IsValid(ncr, true)) { conceded += failed.Quantity; continue; }
                if (ncr.Status != NcrStatus.Closed && ncr.Status != NcrStatus.Cancelled)
                    return head + $"اجرای {failed.Number} مردود شده است؛ گزارش عدم انطباق {ncr.Number} هنوز بسته نشده است.";
            }

            List<SpRun> passed = runs.Where(r => r.Result == SpRunResult.Pass).ToList();
            SpRun waiting = passed.FirstOrDefault(r => r.ReviewStatus == SpReviewStatus.None);
            if (waiting != null) return head + $"اجرای {waiting.Number} هنوز توسط QA بازبینی نشده است (بازبین باید غیر از ثبت‌کننده و اپراتور باشد).";
            List<SpRun> reviewed = passed.Where(r => r.ReviewStatus == SpReviewStatus.Approved).ToList();
            if (reviewed.Count == 0) return head + "هیچ اجرای قبول و بازبینی‌شده‌ای ندارد.";

            decimal covered = reviewed.Sum(r => r.Quantity) + conceded;
            if (covered + scrap < required)
                return head + $"اجرای قبول و بازبینی‌شده فقط {SpecialProcessRules.Num(covered)} از {SpecialProcessRules.Num(required)} قطعه را پوشش می‌دهد.";

            foreach (SpRun run in reviewed)
            {
                if (outside || run.IsSubcontract)
                {
                    if (!run.IsSubcontract) return head + $"اجرای {run.Number} اجرای داخلی است ولی گام برون‌سپاری شده است.";
                    string evidence = EvidenceProblem(run, settings);
                    if (evidence != null) return head + evidence;
                    if (supplierId.HasValue && run.SupplierId != supplierId) return head + $"پیمانکار اجرای {run.Number} با پیمانکار گام یکی نیست.";
                    string supplier = await SupplierProblemAsync(uow, run.SupplierId, type, run.EndedAt, ct);
                    if (supplier != null) return head + supplier;
                    continue;
                }
                if (run.IsSubcontract) return head + $"اجرای {run.Number} اجرای پیمانکار است ولی گام داخلی است.";
                if (!run.QualificationId.HasValue) return head + $"اجرای {run.Number} صلاحیت فرایند ندارد.";
                SpQualification q = await uow.Set<SpQualification>().AsNoTracking().Include(x => x.Equipments).Include(x => x.Operators).FirstOrDefaultAsync(x => x.Id == run.QualificationId.Value && !x.Deleted, ct);
                string problem = QualificationProblem(q, run.StartedAt, run.EndedAt);
                if (problem != null) return head + problem;
                if (q.TypeId != type.Id) return head + $"صلاحیت {q.Number} برای فرایند دیگری است.";
                if (!run.OperatorPersonId.HasValue) return head + $"اجرای {run.Number} اپراتور ندارد.";
                if (!q.Operators.Any(o => o.PersonId == run.OperatorPersonId.Value)) return head + $"اپراتور اجرای {run.Number} جزو اپراتورهای صلاحیت {q.Number} نیست.";
                QualifiedPerson person = await uow.Set<QualifiedPerson>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == run.OperatorPersonId.Value, ct);
                problem = await CompetenceProblemAsync(uow, person, type, run.StartedAt, person?.Name ?? "اپراتور", ct);
                if (problem != null) return head + problem;
                problem = await EquipmentProblemAsync(uow, q, type, run.MachineId, run.Instruments.Select(i => i.EquipmentId).ToList(), run.StartedAt, ct);
                if (problem != null) return head + problem;
            }

            // the people who booked run time on the step are qualified too (the operator of the run is not the only one who touched the pieces)
            if (!outside && operationId.HasValue)
            {
                var booked = await uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.OperationId == operationId.Value && (b.Kind == MfBookingKind.Run || b.Kind == MfBookingKind.Rework))
                    .Select(b => new { b.EmployeeId, b.StartAt }).ToListAsync(ct);
                foreach (var group in booked.GroupBy(b => b.EmployeeId))
                {
                    FnEmployee employee = await uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == group.Key, ct);
                    QualifiedPerson person = await PersonOfAsync(uow, employee, ct);
                    string who = MfNames.EmployeeName(employee) ?? ("#" + group.Key);
                    string problem = await CompetenceProblemAsync(uow, person, type, group.Min(b => b.StartAt).Date, who, ct);
                    if (problem != null) return head + problem;
                }
            }
            return null;
        }

        /// <summary>A flagged step is not skipped: its qualification and run record are the proof the product got the process. Engineering removes the step from the routing instead.</summary>
        public static async Task<string> SkipProblemAsync(IUnitOfWork uow, MfOperation op, CancellationToken ct)
        {
            if (op == null || !op.IsSpecialProcess || !op.SpecialProcessTypeId.HasValue) return null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            if (!On(settings, QualitySettingKeys.PkcSpEnforce)) return null;
            SpProcessType type = await uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == op.SpecialProcessTypeId.Value, ct);
            if (type == null || type.ExistingControl) return null;
            return $"گام «{op.Title}» فرایند ویژه «{type.Title}» است و رد نمی‌شود؛ اگر فرایند لازم نیست، مهندسی آن را از مسیر ساخت حذف کند.";
        }

        /// <summary>The start of a booking that does the work of a flagged step: only an employee with a valid operator competence record for the process starts it.</summary>
        public static async Task<string> StartProblemAsync(IUnitOfWork uow, MfOperation op, FnEmployee employee, MfBookingKind kind, DateTime on, CancellationToken ct)
        {
            if (op == null || !op.IsSpecialProcess || !op.SpecialProcessTypeId.HasValue) return null;
            if (kind != MfBookingKind.Run && kind != MfBookingKind.Rework) return null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            if (!On(settings, QualitySettingKeys.PkcSpEnforce)) return null;
            SpProcessType type = await uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == op.SpecialProcessTypeId.Value, ct);
            if (type == null || type.ExistingControl || op.IsOutside) return null;
            QualifiedPerson person = await PersonOfAsync(uow, employee, ct);
            return await CompetenceProblemAsync(uow, person, type, on, MfNames.EmployeeName(employee) ?? "کارمند", ct);
        }

        // ================================================================== subcontract receipt

        /// <summary>
        /// The subcontract receipt: for every flagged outside step this receipt brings back, the evidence of the supplier (a passed run with the certificate and chart, the supplier approved for the process) must
        /// exist before QC accepts. Null = nothing missing (or no special process step is on the receipt).
        /// </summary>
        public static async Task<string> ReceiptEvidenceProblemAsync(IUnitOfWork uow, InventoryDocument receipt, CancellationToken ct)
        {
            if (receipt == null || receipt.Type != InventoryDocumentType.SubcontractReturn || !receipt.WorkOrderId.HasValue) return null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            if (!On(settings, QualitySettingKeys.PkcSpEnforce)) return null;
            int workOrderId = receipt.WorkOrderId.Value;
            string reference = QualityText.Clean(receipt.ReferenceNumber);
            var candidates = await (from o in uow.Set<MfOperation>().AsNoTracking()
                                    join po in uow.Set<MfProductionOrder>().AsNoTracking() on o.OrderId equals po.Id
                                    where !o.Deleted && o.IsOutside && o.IsSpecialProcess && o.SpecialProcessTypeId != null && po.WorkOrderId == workOrderId && po.Status != MfOrderStatus.Cancelled
                                    select new { Op = o, po.Number }).ToListAsync(ct);
            foreach (var c in candidates)
            {
                MfOperation op = c.Op;
                if (op.OutsideSupplierId.HasValue && receipt.ProviderId.HasValue && op.OutsideSupplierId != receipt.ProviderId) continue;
                string job = MfNames.Operation(c.Number, op.Sequence);
                string issueNumber = op.SubcontractIssueId.HasValue ? await uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.Id == op.SubcontractIssueId.Value).Select(d => d.Number).FirstOrDefaultAsync(ct) : null;
                bool linked = reference != null && (string.Equals(reference, job, StringComparison.OrdinalIgnoreCase) || (issueNumber != null && string.Equals(reference, issueNumber, StringComparison.OrdinalIgnoreCase)));
                if (op.Status == MfOperationStatus.Done || op.Status == MfOperationStatus.Skipped || op.Status == MfOperationStatus.Rejected) continue;
                // Fix round 1 (M2): a return of the same supplier on a work order with a live flagged outside step is tied to that step by its reference; an unlinked reference is refused, not skipped
                if (!linked) return $"کالای برگشتی پیمانکار برای گام {job} ({op.Title}) فرایند ویژه است؛ شماره مرجع سند باید شماره کار {job}" + (issueNumber != null ? $" یا شماره حواله ارسال {issueNumber}" : string.Empty) + " باشد.";
                SpProcessType type = await uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == op.SpecialProcessTypeId.Value, ct);
                if (type == null || type.ExistingControl) continue;
                string head = $"کالای برگشتی برای گام {job} ({op.Title}) فرایند ویژه «{type.Title}» است: ";
                List<SpRun> runs = await uow.Set<SpRun>().AsNoTracking().Where(r => !r.Deleted && r.OperationId == op.Id && r.Status == SpRunStatus.Recorded && r.IsSubcontract).ToListAsync(ct);
                SpRun good = runs.Where(r => r.Result == SpRunResult.Pass && r.ReviewStatus == SpReviewStatus.Approved).OrderByDescending(r => r.Id).FirstOrDefault();
                if (good == null) return head + "مدرک اجرای پیمانکار (اجرای قبول با گواهی و نمودار که QA بازبینی کرده باشد) ثبت نشده است؛ پیش از پذیرش QC ثبت و بازبینی شود.";
                string evidence = EvidenceProblem(good, settings);
                if (evidence != null) return head + evidence;
                string supplier = await SupplierProblemAsync(uow, good.SupplierId ?? receipt.ProviderId, type, good.EndedAt, ct);
                if (supplier != null) return head + supplier;
            }
            return null;
        }

        /// <summary>The supplier of an outside special step must be approved for the process before material is sent: null when the step is not flagged or the supplier is approved.</summary>
        public static async Task<string> OutsideSupplierProblemAsync(IUnitOfWork uow, MfOperation op, CancellationToken ct)
        {
            if (op == null || !op.IsOutside || !op.IsSpecialProcess || !op.SpecialProcessTypeId.HasValue) return null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            if (!On(settings, QualitySettingKeys.PkcSpEnforce)) return null;
            SpProcessType type = await uow.Set<SpProcessType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == op.SpecialProcessTypeId.Value, ct);
            if (type == null || type.ExistingControl) return null;
            return await SupplierProblemAsync(uow, op.OutsideSupplierId, type, DateTime.Today, ct);
        }
    }
}
