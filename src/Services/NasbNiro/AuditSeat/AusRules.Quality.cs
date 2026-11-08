using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Quality;

namespace Services.NasbNiro.AuditSeat
{
    // Rules of the processes CALIBRATION, PERSONNEL, QUALITY, SPECIAL and PRODUCTION (quality records).
    public static partial class AusCatalog
    {
        private static decimal Q(AusContext c, string key, decimal fallback) => c.Settings != null && c.Settings.TryGetValue(key, out decimal v) ? v : fallback;

        private static bool ValidAt(Qualification q, DateTime on) => !q.Revoked && q.IssuedDate.Date <= on.Date && (!q.ExpiryDate.HasValue || q.ExpiryDate.Value.Date >= on.Date);

        private static IEnumerable<AusRuleDef> QualityRules()
        {
            // ------------------------------------------------------------------------------------------ calibration
            yield return Def("AUD-CAL-001", "CALIBRATION", AusSeverity.Major, "تجهیز اندازه‌گیری نیازمند کالیبراسیون، با سررسید گذشته یا بدون کالیبراسیون و در خدمت", Iso9, "7.1.5.2", "WI-QC-001 / FO-QC-007..011 (کالیبراسیون)",
                "MeasuringEquipment requiring calibration, in service, NextDue null or before today, without an approved waiver", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(c.Uow.Set<MeasuringEquipment>().AsNoTracking().Where(e => !e.Deleted && e.RequiresCalibration && e.Status != EquipmentStatus.OutOfService && e.Status != EquipmentStatus.NotRequired
                            && e.CalibrationWaivedAt == null && (e.NextDue == null || e.NextDue < today)).OrderBy(e => e.Id)
                        .Select(e => new { e.Id, e.Code, e.Name, e.NextDue, e.LastCalibrated, e.OwnerUnit, e.CreatorUserId }));
                    return rows.Where(e => !c.FocusId.HasValue || e.Id == c.FocusId.Value).Select(e =>
                        Hit("MeasuringEquipment", e.Id, e.Code, e.NextDue.HasValue ? $"تجهیز {e.Code} ({e.Name}) سررسید کالیبراسیون {P(e.NextDue)} را گذرانده و هنوز در خدمت است." : $"تجهیز {e.Code} ({e.Name}) نیازمند کالیبراسیون است ولی هرگز کالیبره نشده است.", link: "/quality/equipment")
                            .Fact("code", e.Code).Fact("nextDue", e.NextDue).Fact("lastCalibrated", e.LastCalibrated).Fact("unit", e.OwnerUnit)).ToList();
                });

            yield return Def("AUD-CAL-002", "CALIBRATION", AusSeverity.Major, "اندازه‌گیری با تجهیزی انجام شده که در تاریخ اندازه‌گیری کالیبراسیون معتبر نداشته است", Iso9, "7.1.5.2", "WI-QC-001 / PR-QC-003",
                "QcMeasurement (record created since the last pass) whose equipment had no calibration event covering the record date (EventDate <= date <= NextDue)", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var ms = await c.BoundedAsync(
                        from m in c.Uow.Set<QcMeasurement>().AsNoTracking()
                        join r in c.Uow.Set<QcRecord>().AsNoTracking() on m.RecordId equals r.Id
                        join e in c.Uow.Set<MeasuringEquipment>().AsNoTracking() on m.EquipmentId.Value equals e.Id
                        where m.EquipmentId != null && !r.Deleted && r.CreateDate >= since && e.RequiresCalibration
                        orderby m.Id
                        select new { m.Id, m.EquipmentId, m.Characteristic, RecordId = r.Id, r.Number, r.RecordDate, r.InspectorUserId, e.Code });
                    var hits = new List<AusHit>();
                    if (ms.Count == 0) return hits;
                    List<int> eq = ms.Select(m => m.EquipmentId.Value).Distinct().ToList();
                    var events = (await c.Uow.Set<CalibrationEvent>().AsNoTracking().Where(x => !x.Deleted && eq.Contains(x.EquipmentId)).Select(x => new { x.EquipmentId, x.EventDate, x.NextDue }).ToListAsync(c.Ct)).ToLookup(x => x.EquipmentId);
                    foreach (var m in ms)
                    {
                        if (c.FocusId.HasValue && m.RecordId != c.FocusId.Value) continue;
                        var prior = events[m.EquipmentId.Value].Where(x => x.EventDate.Date <= m.RecordDate.Date).OrderByDescending(x => x.EventDate).FirstOrDefault();
                        bool bad = prior == null || (prior.NextDue.HasValue && prior.NextDue.Value.Date < m.RecordDate.Date);
                        if (!bad) continue;
                        hits.Add(Hit("QcRecord", m.RecordId, m.Number, prior == null
                                ? $"در بازرسی {m.Number} ({P(m.RecordDate)}) تجهیز {m.Code} به کار رفته که پیش از آن تاریخ هیچ کالیبراسیونی ندارد."
                                : $"در بازرسی {m.Number} ({P(m.RecordDate)}) تجهیز {m.Code} به کار رفته که کالیبراسیونش در {P(prior.NextDue)} منقضی شده بود.", "eq" + m.EquipmentId, owner: m.InspectorUserId, link: "/quality/records")
                            .Fact("equipment", m.Code).Fact("recordDate", m.RecordDate).Fact("lastCalibration", prior?.EventDate).Fact("validUntil", prior?.NextDue));
                    }
                    return hits;
                });

            yield return Def("AUD-CAL-003", "CALIBRATION", AusSeverity.Minor, "ارزیابی اثر کالیبراسیون خارج از تلورانس (OOT) انجام نشده است", Iso9, "7.1.5.2", "WI-QC-001 (خارج از تلورانس)",
                "OutOfToleranceItem still Pending in an open OutOfToleranceCase", false, async c =>
                {
                    var rows = await c.BoundedAsync(
                        from i in c.Uow.Set<OutOfToleranceItem>().AsNoTracking()
                        join k in c.Uow.Set<OutOfToleranceCase>().AsNoTracking() on i.CaseId equals k.Id
                        where !k.Deleted && k.Status == OotStatus.Open && i.Assessment == OotAssessment.Pending
                        orderby i.Id select new { i.Id, i.SourceKind, i.SourceNumber, i.SourceDate, CaseNumber = k.Number, k.EquipmentId });
                    return rows.Where(i => !c.FocusId.HasValue || i.Id == c.FocusId.Value).Select(i =>
                        Hit("OutOfToleranceItem", i.Id, i.SourceNumber, $"سند {i.SourceNumber} ({i.SourceKind}، {P(i.SourceDate)}) در پرونده‌ی خارج از تلورانس {i.CaseNumber} هنوز ارزیابی اثر نشده است.", link: "/quality/equipment")
                            .Fact("case", i.CaseNumber).Fact("source", i.SourceNumber).Fact("equipmentId", i.EquipmentId)).ToList();
                });

            // ------------------------------------------------------------------------------------------ qualifications
            yield return Def("AUD-QL-001", "SPECIAL", AusSeverity.Major, "جوش ثبت‌شده با جوشکاری که صلاحیتش در تاریخ جوش منقضی، لغوشده یا هنوز صادرنشده بود", "ASME BPVC IX", "QW-322", "WI-QC-010 (جوشکاری و پرسنل واجد صلاحیت)",
                "WeldLog whose welder qualification was revoked, not yet issued or expired on the weld date", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(
                        from w in c.Uow.Set<WeldLog>().AsNoTracking()
                        join q in c.Uow.Set<Qualification>().AsNoTracking() on w.QualificationId equals q.Id
                        join p in c.Uow.Set<QualifiedPerson>().AsNoTracking() on q.PersonId equals p.Id
                        where !w.Deleted && w.CreateDate >= since && (q.Revoked || q.IssuedDate > w.WeldDate || (q.ExpiryDate != null && q.ExpiryDate < w.WeldDate))
                        orderby w.Id select new { w.Id, w.WeldDate, w.JointNo, w.WorkOrderId, w.CreatorUserId, q.ExpiryDate, q.Revoked, q.IssuedDate, p.PersonnelCode, q.Scope });
                    return rows.Where(w => !c.FocusId.HasValue || w.Id == c.FocusId.Value).Select(w =>
                        Hit("WeldLog", w.Id, w.JointNo, $"جوش {w.JointNo} (دستور کار {w.WorkOrderId}، {P(w.WeldDate)}) با جوشکار {w.PersonnelCode} ثبت شده که {(w.Revoked ? "صلاحیتش لغو شده" : w.IssuedDate > w.WeldDate ? "صلاحیتش هنوز صادر نشده" : "صلاحیتش در " + P(w.ExpiryDate) + " منقضی شده")} بود.", owner: w.CreatorUserId, link: "/quality/welding")
                            .Fact("welder", w.PersonnelCode).Fact("weldDate", w.WeldDate).Fact("expiry", w.ExpiryDate).Fact("revoked", w.Revoked).Fact("scope", w.Scope)).ToList();
                });

            yield return Def("AUD-QL-002", "SPECIAL", AusSeverity.Major, "گزارش آزمون با آزمونگری که صلاحیتش در تاریخ گزارش معتبر نبود", "ISO 9712 / ASNT SNT-TC-1A", "صلاحیت پرسنل NDT", "WI-QC-002/003/006/008",
                "TestReport (submitted/approved) whose examiner qualification was revoked, not issued or expired on the report date", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(
                        from t in c.Uow.Set<TestReport>().AsNoTracking()
                        join q in c.Uow.Set<Qualification>().AsNoTracking() on t.ExaminerQualificationId.Value equals q.Id
                        where !t.Deleted && t.Status != TestReportStatus.Draft && t.CreateDate >= since && (q.Revoked || q.IssuedDate > t.ReportDate || (q.ExpiryDate != null && q.ExpiryDate < t.ReportDate))
                        orderby t.Id select new { t.Id, t.Number, t.ReportDate, t.ExaminerUserId, q.ExpiryDate, q.Revoked, q.Method });
                    return rows.Where(t => !c.FocusId.HasValue || t.Id == c.FocusId.Value).Select(t =>
                        Hit("TestReport", t.Id, t.Number, $"گزارش آزمون {t.Number} ({P(t.ReportDate)}) را آزمونگری امضا کرده که صلاحیتش ({t.Method}) {(t.Revoked ? "لغو شده" : "در " + P(t.ExpiryDate) + " منقضی شده")} بود.", owner: t.ExaminerUserId, link: "/quality/test-reports")
                            .Fact("reportDate", t.ReportDate).Fact("expiry", t.ExpiryDate).Fact("revoked", t.Revoked).Fact("method", t.Method)).ToList();
                });

            yield return Def("AUD-QL-003", "SPECIAL", AusSeverity.Major, "اجرای فرایند ویژه با اپراتوری که صلاحیت معتبر نداشت", Iso9, "7.2 / 8.5.1", "برنامه بسته C (فرایندهای ویژه)",
                "SpRun (recorded) whose operator had no valid SpecialProcessOperator qualification on the start date", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var runs = await c.BoundedAsync(c.Uow.Set<SpRun>().AsNoTracking().Where(r => !r.Deleted && r.Status == SpRunStatus.Recorded && r.OperatorPersonId != null && r.CreateDate >= since).OrderBy(r => r.Id)
                        .Select(r => new { r.Id, r.Number, r.OperatorPersonId, r.StartedAt, r.CreatorUserId }));
                    var hits = new List<AusHit>();
                    if (runs.Count == 0) return hits;
                    List<int> persons = runs.Select(r => r.OperatorPersonId.Value).Distinct().ToList();
                    var quals = (await c.Uow.Set<Qualification>().AsNoTracking().Where(q => !q.Deleted && q.Kind == QualificationKind.SpecialProcessOperator && persons.Contains(q.PersonId)).ToListAsync(c.Ct)).ToLookup(q => q.PersonId);
                    var codes = await c.Uow.Set<QualifiedPerson>().AsNoTracking().Where(p => persons.Contains(p.Id)).Select(p => new { p.Id, p.PersonnelCode }).ToListAsync(c.Ct);
                    foreach (var r in runs)
                    {
                        if (c.FocusId.HasValue && r.Id != c.FocusId.Value) continue;
                        if (quals[r.OperatorPersonId.Value].Any(q => ValidAt(q, r.StartedAt))) continue;
                        string code = codes.FirstOrDefault(x => x.Id == r.OperatorPersonId)?.PersonnelCode ?? r.OperatorPersonId.ToString();
                        hits.Add(Hit("SpRun", r.Id, r.Number, $"اجرای فرایند ویژه {r.Number} ({P(r.StartedAt)}) با اپراتور {code} ثبت شده که صلاحیت معتبر فرایند ویژه نداشت.", owner: r.CreatorUserId, link: "/quality/special-processes")
                            .Fact("operator", code).Fact("startedAt", r.StartedAt).Fact("qualifications", quals[r.OperatorPersonId.Value].Count()));
                    }
                    return hits;
                });

            yield return Def("AUD-QL-004", "SPECIAL", AusSeverity.Minor, "بازرسی ثبت‌شده با بازرسی که گواهی صلاحیت بازرس او در آن تاریخ معتبر نبود", Iso9, "7.2", "PR-QC-003 (سوابق بازرسی)",
                "QcRecord whose inspector (a QualifiedPerson with Inspector qualifications) had none valid on the record date", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var recs = await c.BoundedAsync(c.Uow.Set<QcRecord>().AsNoTracking().Where(r => !r.Deleted && r.CreateDate >= since).OrderBy(r => r.Id).Select(r => new { r.Id, r.Number, r.InspectorUserId, r.RecordDate }));
                    var hits = new List<AusHit>();
                    if (recs.Count == 0) return hits;
                    List<int> users = recs.Select(r => r.InspectorUserId).Distinct().ToList();
                    var people = await c.Uow.Set<QualifiedPerson>().AsNoTracking().Where(p => !p.Deleted && p.UserId != null && users.Contains(p.UserId.Value)).Select(p => new { p.Id, p.UserId, p.PersonnelCode }).ToListAsync(c.Ct);
                    if (people.Count == 0) return hits;
                    List<int> pids = people.Select(p => p.Id).ToList();
                    var quals = (await c.Uow.Set<Qualification>().AsNoTracking().Where(q => !q.Deleted && q.Kind == QualificationKind.Inspector && pids.Contains(q.PersonId)).ToListAsync(c.Ct)).ToLookup(q => q.PersonId);
                    foreach (var r in recs)
                    {
                        if (c.FocusId.HasValue && r.Id != c.FocusId.Value) continue;
                        var p = people.FirstOrDefault(x => x.UserId == r.InspectorUserId);
                        if (p == null || !quals[p.Id].Any()) continue;   // a person without inspector certificates is not judged here
                        if (quals[p.Id].Any(q => ValidAt(q, r.RecordDate))) continue;
                        hits.Add(Hit("QcRecord", r.Id, r.Number, $"بازرسی {r.Number} ({P(r.RecordDate)}) را بازرسی انجام داده که گواهی صلاحیت بازرس او در آن روز معتبر نبود.", "insp", owner: r.InspectorUserId, link: "/quality/records").Fact("inspector", p.PersonnelCode).Fact("recordDate", r.RecordDate));
                    }
                    return hits;
                });

            yield return Def("AUD-QL-005", "PERSONNEL", AusSeverity.Minor, "گواهی صلاحیت یا آموزش پرسنل فعال منقضی شده و تمدید نشده است", Iso9, "7.2", "WI-QC-010 / FO-QA (صلاحیت و آموزش)",
                "Qualification (not revoked, not superseded, not renewed) of an active QualifiedPerson with ExpiryDate before today", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(
                        from q in c.Uow.Set<Qualification>().AsNoTracking()
                        join p in c.Uow.Set<QualifiedPerson>().AsNoTracking() on q.PersonId equals p.Id
                        where !q.Deleted && !q.Revoked && !q.Superseded && q.ExpiryDate != null && q.ExpiryDate < today && p.IsActive && !p.Deleted
                            && !c.Uow.Set<Qualification>().Any(n => n.RenewsQualificationId == q.Id && !n.Revoked && !n.Deleted)
                        orderby q.Id select new { q.Id, q.Kind, q.Method, q.CertificateNumber, q.ExpiryDate, p.PersonnelCode, p.UserId });
                    return rows.Where(q => !c.FocusId.HasValue || q.Id == c.FocusId.Value).Select(q =>
                        Hit("Qualification", q.Id, q.CertificateNumber, $"گواهی صلاحیت {q.Kind} ({q.Method}) پرسنل {q.PersonnelCode} در {P(q.ExpiryDate)} منقضی شده و تمدید نشده است.", owner: q.UserId, link: "/quality/personnel")
                            .Fact("person", q.PersonnelCode).Fact("kind", q.Kind).Fact("expiry", q.ExpiryDate)).ToList();
                });

            // ------------------------------------------------------------------------------------------ nonconformance and corrective action
            yield return Def("AUD-NC-001", "QUALITY", AusSeverity.Minor, "گزارش عدم انطباق (NCR) بیش از مهلت قانونی باز است", Iso9, "8.7 / 10.2", "PR-QC-002 / FO-QC-013 (NCR)",
                "NonConformance not closed/cancelled past ReportDate + NcrMinorDueDays / NcrMajorDueDays (Major when the NCR is major)", false, async c =>
                {
                    DateTime minorLimit = c.Today.AddDays(-(double)Q(c, QualitySettingKeys.NcrMinorDays, 14)), majorLimit = c.Today.AddDays(-(double)Q(c, QualitySettingKeys.NcrMajorDays, 7));
                    var rows = await c.BoundedAsync(c.Uow.Set<NonConformance>().AsNoTracking().Where(n => !n.Deleted && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled
                            && ((n.Severity == NcrSeverity.Major && n.ReportDate < majorLimit) || (n.Severity != NcrSeverity.Major && n.ReportDate < minorLimit))).OrderBy(n => n.Id)
                        .Select(n => new { n.Id, n.Number, n.ReportDate, n.Severity, n.Status, n.CreatorUserId }));
                    return rows.Where(n => !c.FocusId.HasValue || n.Id == c.FocusId.Value).Select(n =>
                    {
                        AusHit h = Hit("NonConformance", n.Id, n.Number, $"عدم انطباق {n.Number} از {P(n.ReportDate)} هنوز بسته نشده و از مهلت گذشته است.", owner: n.CreatorUserId, link: "/quality/ncr").Fact("reportDate", n.ReportDate).Fact("severity", n.Severity).Fact("status", n.Status);
                        if (n.Severity == NcrSeverity.Major) h.Severity = AusSeverity.Major;
                        return h;
                    }).ToList();
                });

            yield return Def("AUD-NC-002", "QUALITY", AusSeverity.Minor, "اقدام اصلاحی (CAPA) از سررسید اجرا یا بررسی اثربخشی گذشته است", Iso9, "10.2.1", "FO-QA-017 (CAPA)",
                "Capa open/defined past DueDate, or implemented/effectiveness-check past EffectivenessDueDate (Major when mandatory)", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(c.Uow.Set<Capa>().AsNoTracking().Where(x => !x.Deleted && (((x.Status == CapaStatus.Open || x.Status == CapaStatus.ActionsDefined) && x.DueDate < today)
                            || ((x.Status == CapaStatus.Implemented || x.Status == CapaStatus.EffectivenessCheck) && x.EffectivenessDueDate < today))).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.Number, x.Status, x.DueDate, x.EffectivenessDueDate, x.Mandatory, x.OwnerUserId }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                    {
                        AusHit h = Hit("Capa", x.Id, x.Number, $"اقدام اصلاحی {x.Number} ({x.Status}) از سررسید {P(x.Status == CapaStatus.Open || x.Status == CapaStatus.ActionsDefined ? x.DueDate : x.EffectivenessDueDate)} گذشته است.", owner: x.OwnerUserId, link: "/quality/capa")
                            .Fact("status", x.Status).Fact("due", x.DueDate).Fact("effectivenessDue", x.EffectivenessDueDate).Fact("mandatory", x.Mandatory);
                        if (x.Mandatory) h.Severity = AusSeverity.Major;
                        return h;
                    }).ToList();
                });

            yield return Def("AUD-NC-003", "QUALITY", AusSeverity.Major, "اقدام اصلاحی بسته شده بدون ثبت نتیجه‌ی بررسی اثربخشی", Iso9, "10.2.1 (e,f)", "FO-QA-017 (CAPA)",
                "Capa Closed with an empty EffectivenessResult", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Uow.Set<Capa>().AsNoTracking().Where(x => !x.Deleted && x.Status == CapaStatus.Closed && x.ModifyDate >= since && (x.EffectivenessResult == null || x.EffectivenessResult == "")).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.Number, x.ClosedAt, x.ClosedByUserId, x.OwnerUserId }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                        Hit("Capa", x.Id, x.Number, $"اقدام اصلاحی {x.Number} در {P(x.ClosedAt)} بسته شده ولی نتیجه‌ی بررسی اثربخشی ثبت نشده است.", "noeffect", owner: x.OwnerUserId, link: "/quality/capa").Fact("closedAt", x.ClosedAt).Fact("closedBy", x.ClosedByUserId)).ToList();
                });

            yield return Def("AUD-NC-004", "QUALITY", AusSeverity.Minor, "شکایت مشتری از مهلت پاسخ گذشته و بی‌پاسخ مانده است", Iso9, "9.1.2 / 10.2", "PR-QC-002 (شکایت مشتری)",
                "CustomerComplaint Received/UnderReview with ResponseDue before today", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(c.Uow.Set<CustomerComplaint>().AsNoTracking().Where(x => !x.Deleted && (x.Status == ComplaintStatus.Received || x.Status == ComplaintStatus.UnderReview) && x.ResponseDue < today).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.Number, x.ResponseDue, x.Severity, x.CreatorUserId }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                        Hit("CustomerComplaint", x.Id, x.Number, $"شکایت مشتری {x.Number} تا مهلت {P(x.ResponseDue)} پاسخ نگرفته است.", owner: x.CreatorUserId, link: "/quality/complaints").Fact("responseDue", x.ResponseDue).Fact("severity", x.Severity)).ToList();
                });

            // ------------------------------------------------------------------------------------------ special processes, materials, first article, as-built
            yield return Def("AUD-SP-001", "SPECIAL", AusSeverity.Major, "گام فرایند ویژه پایان یافته ولی هیچ اجرای ثبت‌شده‌ای ندارد (انحراف داده)", Iso9, "8.5.1 (f)", "برنامه بسته C (پایان گام فقط با اجرای قبول)",
                "MfOperation IsSpecialProcess, Done, with no SpRun (recorded) for the operation", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && o.IsSpecialProcess && o.Status == MfOperationStatus.Done && o.ModifyDate >= since
                            && !c.Uow.Set<SpRun>().Any(r => r.OperationId == o.Id && r.Status == SpRunStatus.Recorded && !r.Deleted)).OrderBy(o => o.Id)
                        .Select(o => new { o.Id, o.OrderId, o.Title, o.Sequence, o.FinishedAt, o.CreatorUserId, o.SpecialProcessTypeId }));
                    return rows.Where(o => !c.FocusId.HasValue || o.Id == c.FocusId.Value).Select(o =>
                        Hit("MfOperation", o.Id, o.Title, $"گام {o.Sequence} ({o.Title}) از دستور تولید {o.OrderId} فرایند ویژه است و پایان یافته ولی اجرای ثبت‌شده‌ای برای آن نیست.", owner: o.CreatorUserId, link: "/production")
                            .Fact("order", o.OrderId).Fact("sequence", o.Sequence).Fact("finishedAt", o.FinishedAt).Fact("type", o.SpecialProcessTypeId)).ToList();
                });

            yield return Def("AUD-SP-002", "SPECIAL", AusSeverity.Minor, "اجرای فرایند ویژه ثبت‌شده ولی بازبینی دوم (QA) نشده است", Iso9, "8.5.1 (f)", "برنامه بسته C (بازبینی نمودار و اجرا)",
                "SpRun recorded with ReviewStatus None for longer than Audit.T.ExceptionPendingDays", false, async c =>
                {
                    DateTimeOffset limit = c.Now.AddDays(-c.Days(AusSettingKeys.ExceptionPendingDays));
                    var rows = await c.BoundedAsync(c.Uow.Set<SpRun>().AsNoTracking().Where(r => !r.Deleted && r.Status == SpRunStatus.Recorded && r.ReviewStatus == SpReviewStatus.None && r.CreateDate < limit).OrderBy(r => r.Id)
                        .Select(r => new { r.Id, r.Number, r.StartedAt, r.CreatorUserId }));
                    return rows.Where(r => !c.FocusId.HasValue || r.Id == c.FocusId.Value).Select(r =>
                        Hit("SpRun", r.Id, r.Number, $"اجرای فرایند ویژه {r.Number} ({P(r.StartedAt)}) هنوز بازبینی دوم نشده است.", "review", owner: r.CreatorUserId, link: "/quality/special-processes").Fact("startedAt", r.StartedAt)).ToList();
                }, "QA");

            yield return Def("AUD-MT-001", "SPECIAL", AusSeverity.Major, "گواهی ماده‌ی بچ پذیرفته‌شده تایید یا قبول نشده است", Iso9, "8.4.2 / 8.6", "WI-WH-001 / PR-QC-002 (گواهی EN 10204)",
                "InventoryLot accepted by an incoming QcInspection whose MaterialCertificates are none accepted+verified (or only a legacy certificate number exists)", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(
                        from i in c.Uow.Set<QcInspection>().AsNoTracking()
                        join l in c.Uow.Set<InventoryLot>().AsNoTracking() on i.LotId.Value equals l.Id
                        where !i.Deleted && i.Status == InspectionStatus.Done && i.AcceptedQuantity > 0 && i.LotId != null && i.ModifyDate >= since
                            && !c.Uow.Set<MaterialCertificate>().Any(m => m.LotId == l.Id && !m.Deleted && m.Result == CertificateResult.Accepted && m.VerifiedAt != null)
                            && (c.Uow.Set<MaterialCertificate>().Any(m => m.LotId == l.Id && !m.Deleted) || (l.CertificateNumber != null && l.CertificateNumber != ""))
                        orderby i.Id select new { i.Id, i.Number, LotId = l.Id, l.LotNumber, l.HeatNumber, l.CertificateNumber, i.InspectedByUserId });
                    return rows.Where(i => !c.FocusId.HasValue || i.Id == c.FocusId.Value).Select(i =>
                        Hit("QcInspection", i.Id, i.Number, $"بچ {i.LotNumber} (ذوب {i.HeatNumber}) در بازرسی ورودی {i.Number} پذیرفته شده ولی گواهی ماده‌ی آن تایید و قبول نشده است.", owner: i.InspectedByUserId, link: "/quality/incoming")
                            .Fact("lot", i.LotNumber).Fact("heat", i.HeatNumber).Fact("certificate", i.CertificateNumber)).ToList();
                });

            yield return Def("AUD-DS-001", "DESIGN", AusSeverity.Major, "نقشه‌ی منتشرشده‌ی قطعه‌ی ساخت (Make) فهرست مشخصه‌ی تاییدشده ندارد", "ISO 10007:2017", "6.5", "برنامه بسته B (فهرست مشخصه‌ها)",
                "Released PcDocumentRevision of a deliverable linked to a Make BOM line, with no approved/released QcCharacteristicList for that revision", false, async c =>
                {
                    var rows = await c.BoundedAsync(
                        from r in c.Uow.Set<PcDocumentRevision>().AsNoTracking()
                        join l in c.Uow.Set<PcDocumentLink>().AsNoTracking() on r.DeliverableId equals l.DeliverableId
                        join b in c.Uow.Set<Entities.Model.NasbNiro.ProductBank.PbBomLine>().AsNoTracking() on l.BomLineId.Value equals b.Id
                        where !r.Deleted && r.Status == PcDocStatus.Released && !l.Deleted && l.UnlinkReason == null && b.MakeOrBuy == Entities.Model.NasbNiro.ProductBank.PbMakeOrBuy.Make
                            && !c.Uow.Set<QcCharacteristicList>().Any(k => k.RevisionId == r.Id && !k.Deleted && (k.Status == CharListStatus.Approved || k.Status == CharListStatus.Released))
                        orderby r.Id select new { r.Id, r.DeliverableId, r.Code, b.PartNumber, r.ReleasedByUserId });
                    return rows.GroupBy(x => x.Id).Select(g => g.First()).Where(r => !c.FocusId.HasValue || r.Id == c.FocusId.Value).Select(r =>
                        Hit("PcDocumentRevision", r.Id, r.PartNumber, $"نقشه‌ی منتشرشده‌ی قطعه‌ی ساخت {r.PartNumber} (بازنگری {r.Code}) فهرست مشخصه‌ی تاییدشده ندارد.", "charlist", owner: r.ReleasedByUserId, link: "/quality/characteristics")
                            .Fact("part", r.PartNumber).Fact("revision", r.Code).Fact("deliverable", r.DeliverableId)).ToList();
                }, "ENGINEERING");

            yield return Def("AUD-DS-002", "PRODUCTION", AusSeverity.Major, "بازرسی نمونه‌ی اول (FAI) لازم بود ولی تایید نشده و تولید ادامه یافته است", "ISO 9001:2015 / AS9102", "8.5.1 / 8.6", "برنامه بسته B (FAI)",
                "MfProductionOrder with a Done operation and no approved QcFirstArticle for it, for which FirstArticleGate.EvaluateAsync says Required and not satisfied (candidates bounded to 200 per pass)", false, async c =>
                {
                    var hits = new List<AusHit>();
                    var orders = await c.Uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && (o.Status == MfOrderStatus.InProgress || o.Status == MfOrderStatus.Completed)
                            && c.Uow.Set<MfOperation>().Any(x => x.OrderId == o.Id && x.Status == MfOperationStatus.Done && !x.Deleted)
                            && !c.Uow.Set<QcFirstArticle>().Any(f => f.ProductionOrderId == o.Id && f.Status == FaiStatus.Approved && !f.Deleted)).OrderBy(o => o.Id).Take(200).ToListAsync(c.Ct);
                    foreach (MfProductionOrder o in orders)
                    {
                        if (c.FocusId.HasValue && o.Id != c.FocusId.Value) continue;
                        List<MfOperation> ops = await c.Uow.Set<MfOperation>().AsNoTracking().Where(x => x.OrderId == o.Id && !x.Deleted).ToListAsync(c.Ct);
                        FaiRequirement req = await FirstArticleGate.EvaluateAsync(c.Uow, o, ops, c.Ct);
                        if (req == null || !req.Required || req.Satisfying != null) continue;
                        hits.Add(Hit("MfProductionOrder", o.Id, o.Number, $"دستور تولید {o.Number} نمونه‌ی اول لازم دارد ({req.Trigger}) ولی نمونه‌ی اول تاییدشده‌ای نیست، در حالی که گام‌هایش پایان یافته است.", "fai", owner: o.ReleasedByUserId, link: "/quality/fai")
                            .Fact("trigger", req.Trigger).Fact("part", req.PartNumber).Fact("status", o.Status));
                    }
                    return hits;
                });

            yield return Def("AUD-DS-003", "PRODUCTION", AusSeverity.Major, "ممیزی پیکربندی فیزیکی (PCA) برای یادداشت ترخیص‌شده ناسازگار است", "ISO 10007:2017", "6.5", "برنامه بسته B (PCA پیش از ترخیص)",
                "ReleaseNote Released (since the last pass) for which PhysicalConfigurationAudit.RunAsync is applicable and not passed (bounded to 100 per pass)", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var notes = await c.Uow.Set<ReleaseNote>().AsNoTracking().Where(n => !n.Deleted && n.Status == ReleaseNoteStatus.Released && n.ModifyDate >= since).OrderBy(n => n.Id).Take(100).Select(n => new { n.Id, n.Number, n.WorkOrderId, n.ReleasedByUserId }).ToListAsync(c.Ct);
                    var hits = new List<AusHit>();
                    foreach (var n in notes)
                    {
                        if (c.FocusId.HasValue && n.Id != c.FocusId.Value) continue;
                        PcaResult pca = await PhysicalConfigurationAudit.RunAsync(c.Uow, n.WorkOrderId, c.Ct, n.Id);
                        if (pca.Passed) continue;
                        hits.Add(Hit("ReleaseNote", n.Id, n.Number, $"یادداشت ترخیص {n.Number}: ممیزی پیکربندی فیزیکی اکنون ناسازگار است ({pca.Problems.Count} ردیف): {string.Join("؛ ", pca.Problems.Take(3))}.", owner: n.ReleasedByUserId, link: "/quality/release")
                            .Fact("workOrder", n.WorkOrderId).Fact("problems", pca.Problems.Count).Fact("first", pca.Problems.FirstOrDefault()));
                    }
                    return hits;
                });

            yield return Def("AUD-DS-004", "PRODUCTION", AusSeverity.Major, "اسنپ‌شات As-built جاری با هش ثبت‌شده‌اش نمی‌خواند (دستکاری)", "ISO 10007:2017", "6.6", "برنامه بسته B (As-built)",
                "AsBuiltRecord current/pending whose SHA-256 of SnapshotJson differs from the stored Sha256", false, async c =>
                {
                    var rows = await c.BoundedAsync(c.Uow.Set<AsBuiltRecord>().AsNoTracking().Where(a => !a.Deleted && (a.Status == AsBuiltStatus.Current || a.Status == AsBuiltStatus.Pending)).OrderBy(a => a.Id)
                        .Select(a => new { a.Id, a.ProductCode, a.SerialNumber, a.Version, a.SnapshotJson, a.Sha256, a.CreatorUserId }));
                    return rows.Where(a => (!c.FocusId.HasValue || a.Id == c.FocusId.Value) && !string.Equals(CharacteristicRules.Sha256Hex(a.SnapshotJson), a.Sha256, StringComparison.Ordinal)).Select(a =>
                        Hit("AsBuiltRecord", a.Id, a.SerialNumber ?? a.ProductCode, $"اسنپ‌شات As-built {a.ProductCode} نسخه‌ی {a.Version} با هش ثبت‌شده‌اش نمی‌خواند.", owner: a.CreatorUserId, link: "/quality/as-built").Fact("stored", a.Sha256).Fact("recomputed", CharacteristicRules.Sha256Hex(a.SnapshotJson))).ToList();
                });
        }
    }
}
