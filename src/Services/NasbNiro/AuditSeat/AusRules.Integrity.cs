using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Integrity;

namespace Services.NasbNiro.AuditSeat
{
    // Rules of the processes INTEGRITY (records and security) and GOVERNANCE (two-person control and exceptions).
    public static partial class AusCatalog
    {
        private const string Iso27 = "ISO/IEC 27001:2022";
        private const string Iso9 = "ISO 9001:2015";

        private static IEnumerable<AusRuleDef> IntegrityRules()
        {
            yield return Def("AUD-INT-001", "INTEGRITY", AusSeverity.Major, "راستی‌آزمایی زنجیره‌ی سوابق ممیزی شکسته، ناموفق یا انجام‌نشده", Iso27, "A.8.15", "برنامه بسته D (سوابق ضددستکاری)",
                "latest AuditChainRun is Broken or Error, or no Ok run within Audit.T.ChainRunHours", false, async c =>
                {
                    var hits = new List<AusHit>();
                    IQueryable<AuditChainRun> runs = c.Uow.Set<AuditChainRun>().AsNoTracking();
                    var last = await runs.OrderByDescending(r => r.StartedAt).Select(r => new { r.Id, r.Status, r.StartedAt, r.Message, r.BreakKind, r.BreakEntityType, r.BreakRowId }).FirstOrDefaultAsync(c.Ct);
                    DateTimeOffset? lastOk = await runs.Where(r => r.Status == IntegrityRunStatus.Ok).OrderByDescending(r => r.StartedAt).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefaultAsync(c.Ct);
                    if (last != null && last.Status == IntegrityRunStatus.Broken)
                        hits.Add(Hit("AuditChainBreak", Convert.ToInt32(last.BreakRowId ?? 0), null, $"آخرین راستی‌آزمایی زنجیره‌ی ممیزی ({P(last.StartedAt)}) شکستگی نشان داد: {last.BreakKind} در {last.BreakEntityType} ردیف {last.BreakRowId}.", last.BreakEntityType + "|" + last.BreakKind, link: "/integrity")
                            .Fact("run", last.Id).Fact("status", last.Status).Fact("breakKind", last.BreakKind).Fact("entity", last.BreakEntityType).Fact("row", last.BreakRowId));
                    if (last != null && last.Status == IntegrityRunStatus.Error)
                        hits.Add(Hit("AuditChainRun", 0, null, $"آخرین راستی‌آزمایی زنجیره‌ی ممیزی ({P(last.StartedAt)}) با خطا پایان یافت و نتیجه‌ای نداد.", "error", link: "/integrity").Fact("run", last.Id).Fact("status", last.Status).Fact("message", last.Message));
                    decimal chainHours = c.Tol(AusSettingKeys.ChainRunHours, 24);
                    DateTimeOffset limit = c.Now.AddHours(-(double)chainHours);
                    if (lastOk == null || lastOk.Value < limit)
                        hits.Add(Hit("AuditChainRun", 0, null, lastOk == null ? "هنوز هیچ راستی‌آزمایی موفقی از زنجیره‌ی ممیزی ثبت نشده است." : $"آخرین راستی‌آزمایی موفق زنجیره‌ی ممیزی در {P(lastOk)} بوده و از {chainHours:0} ساعت گذشته است.", "stale", link: "/integrity")
                            .Fact("lastOk", lastOk).Fact("maxHours", chainHours));
                    return hits;
                });

            yield return Def("AUD-INT-002", "INTEGRITY", AusSeverity.Major, "آزمون بازیابی پشتیبان انجام‌نشده، ناموفق یا قدیمی", Iso27, "A.8.13", "برنامه بسته D (آزمون ماهانه بازیابی)",
                "RestoreTestResult: none, latest failed, or latest success older than Audit.T.RestoreTestDays (35)", false, async c =>
                {
                    var hits = new List<AusHit>();
                    IQueryable<RestoreTestResult> q = c.Uow.Set<RestoreTestResult>().AsNoTracking();
                    var last = await q.OrderByDescending(r => r.StartedAt).Select(r => new { r.Id, r.Success, r.StartedAt, r.Message }).FirstOrDefaultAsync(c.Ct);
                    DateTimeOffset? lastOk = await q.Where(r => r.Success).OrderByDescending(r => r.StartedAt).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefaultAsync(c.Ct);
                    if (last == null) hits.Add(Hit("RestoreTestResult", 0, null, "تا امروز هیچ آزمون بازیابی پشتیبان ثبت نشده است.", "never", link: "/integrity").Fact("results", 0));
                    else if (!last.Success) hits.Add(Hit("RestoreTestResult", int.Parse(last.StartedAt.LocalDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)), null, $"آخرین آزمون بازیابی پشتیبان ({P(last.StartedAt)}) ناموفق بود.", "failed", link: "/integrity").Fact("result", last.Id).Fact("message", last.Message));
                    if (last != null && (lastOk == null || lastOk.Value < c.Now.AddDays(-c.Days(AusSettingKeys.RestoreTestDays))))
                        hits.Add(Hit("RestoreTestResult", 0, null, $"آخرین آزمون موفق بازیابی در {P(lastOk)} بوده و از {c.S(AusSettingKeys.RestoreTestDays):0} روز گذشته است.", "stale", link: "/integrity").Fact("lastOk", lastOk).Fact("maxDays", c.S(AusSettingKeys.RestoreTestDays)));
                    return hits;
                });

            yield return Def("AUD-INT-003", "INTEGRITY", AusSeverity.Minor, "انحراف تنظیم امنیتی از پروفایل سخت‌گیرانه‌ی تولید", Iso27, "A.8.9", "برنامه بسته D (پروفایل سخت‌گیرانه)",
                "SafetySettingsService deviations: each setting whose value differs from the strict value (weakening ones are Major)", false, async c =>
                {
                    var hits = new List<AusHit>();
                    ISafetySettingsService svc = c.Services?.GetService<ISafetySettingsService>();
                    if (svc == null) return hits;
                    var r = await svc.GetDeviationsAsync(c.Ct);
                    if (!r.Success || r.Data == null) throw new InvalidOperationException("خواندن انحرافات پروفایل سخت‌گیرانه ممکن نشد: " + r.Message);
                    foreach (var i in r.Data.Items.OrderBy(x => x.Store, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal))
                    {
                        AusHit h = Hit("SafetySetting", 0, i.Key, $"تنظیم «{i.Title}» ({i.Store}/{i.Key}) مقدار {i.Current} دارد ولی مقدار سخت‌گیرانه {i.Strict} است.", i.Store + "|" + i.Key, link: "/integrity/safety")
                            .Fact("store", i.Store).Fact("key", i.Key).Fact("strict", i.Strict).Fact("current", i.Current).Fact("weaker", i.Weaker);
                        if (i.Weaker) h.Severity = AusSeverity.Major;
                        hits.Add(h);
                    }
                    return hits;
                });

            yield return Def("AUD-INT-004", "INTEGRITY", AusSeverity.Major, "پشتیبان‌گیری ناموفق: آزمون بازیابی بدون پشتیبان یا با پشتیبان کهنه", Iso27, "A.8.13", "برنامه بسته D (آزمون ماهانه بازیابی)",
                "RestoreTestResult rows (since the last pass) with no backup file, or whose backup was older than Audit.T.BackupStaleDays when the test ran", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<RestoreTestResult>().AsNoTracking()).Where(r => r.StartedAt >= since).OrderBy(r => r.Id)
                        .Select(r => new { r.Id, r.RunId, r.StartedAt, r.BackupFile, r.BackupTakenAt }));
                    var hits = new List<AusHit>();
                    foreach (var r in rows)
                    {
                        if (c.FocusId.HasValue && r.Id != c.FocusId.Value) continue;
                        if (string.IsNullOrWhiteSpace(r.BackupFile))
                            hits.Add(Hit("RestoreTestResult", r.Id, r.RunId, $"آزمون بازیابی {r.RunId} ({P(r.StartedAt)}) هیچ پشتیبانی پیدا نکرد؛ پشتیبان‌گیری انجام نشده است.", "nobackup", link: "/integrity").On(r.StartedAt).Fact("run", r.RunId));
                        else if (r.BackupTakenAt.HasValue && r.BackupTakenAt.Value < r.StartedAt.AddDays(-(double)c.Tol(AusSettingKeys.BackupStaleDays, 1)))
                            hits.Add(Hit("RestoreTestResult", r.Id, r.RunId, $"پشتیبان مورد استفاده‌ی آزمون {r.RunId} در {P(r.BackupTakenAt)} گرفته شده و هنگام آزمون ({P(r.StartedAt)}) بیش از {c.Tol(AusSettingKeys.BackupStaleDays, 1):0} روز کهنه بود.", "oldbackup", link: "/integrity")
                                .On(r.StartedAt).Fact("backupTakenAt", r.BackupTakenAt).Fact("testAt", r.StartedAt).Fact("file", r.BackupFile));
                    }
                    return hits;
                });
        }

        private static IEnumerable<AusRuleDef> GovernanceRules()
        {
            yield return Def("AUD-MC-001", "GOVERNANCE", AusSeverity.Major, "تغییر مهندسی: یک نفر هم درخواست‌کننده یا تهیه‌کننده و هم تاییدکننده، یا مهندسی و QA یک نفر", Iso27, "A.5.3", "تصمیم D13 (ECO فقط مهندسی + QA، دو نفر متفاوت)",
                "EcOrder (approved/closed) where creator, request originator, engineering approver and QA approver are not four-way distinct among the non-null ones", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(
                        from o in c.Scope(c.Uow.Set<EcOrder>().AsNoTracking())
                        join r in c.Uow.Set<EcRequest>().AsNoTracking() on o.RequestId equals r.Id
                        where !o.Deleted && (o.Status == EcoStatus.Approved || o.Status == EcoStatus.Closed) && o.ModifyDate >= since
                        orderby o.Id
                        select new { o.Id, o.Number, o.CreatorUserId, Eng = o.EngineeringApprovedByUserId, Qa = o.QaApprovedByUserId, Originator = r.RequestedByUserId, o.ModifyDate });
                    var hits = new List<AusHit>();
                    foreach (var o in rows)
                    {
                        if (c.FocusId.HasValue && o.Id != c.FocusId.Value) continue;
                        string who = null;
                        if (o.Eng.HasValue && o.Qa.HasValue && o.Eng == o.Qa) who = "مهندسی و QA یک نفر هستند";
                        else if (o.Eng.HasValue && (o.Eng == o.CreatorUserId || o.Eng == o.Originator)) who = "تاییدکننده‌ی مهندسی همان تهیه‌کننده یا درخواست‌کننده است";
                        else if (o.Qa.HasValue && (o.Qa == o.CreatorUserId || o.Qa == o.Originator)) who = "تاییدکننده‌ی QA همان تهیه‌کننده یا درخواست‌کننده است";
                        if (who == null) continue;
                        hits.Add(Hit("EcOrder", o.Id, o.Number, $"دستور تغییر مهندسی {o.Number}: {who}.", owner: o.CreatorUserId, link: "/eng-change/orders/" + o.Id)
                            .On(o.ModifyDate).Fact("creator", o.CreatorUserId).Fact("originator", o.Originator).Fact("engineeringApprover", o.Eng).Fact("qaApprover", o.Qa));
                    }
                    return hits;
                });

            yield return Def("AUD-MC-002", "GOVERNANCE", AusSeverity.Major, "تغییر تنظیم امنیتی را درخواست‌کننده‌ی خودش تصمیم گرفته است", Iso27, "A.5.3", "برنامه بسته D (تغییر دو نفره تنظیم امنیتی)",
                "SafetySettingChange decided (approved/rejected) by the same user who requested it", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<SafetySettingChange>().AsNoTracking())
                        .Where(x => !x.Deleted && x.DecidedByUserId != null && x.DecidedByUserId == x.RequestedByUserId && x.DecidedAt >= since).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.Key, x.RequestedByUserId, x.DecidedAt }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                        Hit("SafetySettingChange", x.Id, x.Key, $"تغییر تنظیم امنیتی «{x.Key}» را همان کسی که درخواست کرده تصمیم گرفته است.", owner: x.RequestedByUserId, link: "/integrity/safety").On(x.DecidedAt).Fact("key", x.Key).Fact("user", x.RequestedByUserId).Fact("decidedAt", x.DecidedAt)).ToList();
                });

            yield return Def("AUD-MC-003", "GOVERNANCE", AusSeverity.Major, "استثنای کنترل داده را ثبت‌کننده‌ی خودش تایید کرده است", Iso27, "A.5.3", "تصمیم PB8 (استثنا را نفر دوم تایید می‌کند)",
                "OpsException decided by the same user who requested it", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<OpsException>().AsNoTracking())
                        .Where(x => !x.Deleted && x.DecidedByUserId != null && x.DecidedByUserId == x.RequestedByUserId && x.DecidedAt >= since).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.RuleCode, x.DocumentNumber, x.RequestedByUserId, x.DecidedAt }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                        Hit("OpsException", x.Id, x.DocumentNumber, $"استثنای قاعده‌ی {x.RuleCode} برای سند {x.DocumentNumber} را همان کسی که درخواست کرده تصمیم گرفته است.", owner: x.RequestedByUserId, link: "/ops-data/quality?tab=exceptions").On(x.DecidedAt).Fact("rule", x.RuleCode).Fact("user", x.RequestedByUserId)).ToList();
                });

            yield return Def("AUD-MC-004", "GOVERNANCE", AusSeverity.Major, "امضاکنندگان BOM چهار نفر متفاوت نیستند", "ISO 10007:2017", "6.2", "تصمیم D13 (چهار امضاکننده‌ی متفاوت BOM)",
                "PbBomHeader approved/released (not imported) where two of preparer, checker, QC, manufacturing, approver are the same user", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<PbBomHeader>().AsNoTracking())
                        .Where(h => !h.Deleted && !h.ImportedSigned && (h.Status == PbBomStatus.Approved || h.Status == PbBomStatus.Released) && h.ModifyDate >= since
                            && (h.CheckedByUserId == h.PreparedByUserId || h.QcCheckedByUserId == h.PreparedByUserId || h.ManufacturingCheckedByUserId == h.PreparedByUserId || h.ApprovedByUserId == h.PreparedByUserId
                                || (h.QcCheckedByUserId != null && h.QcCheckedByUserId == h.CheckedByUserId) || (h.ManufacturingCheckedByUserId != null && h.ManufacturingCheckedByUserId == h.CheckedByUserId) || (h.ApprovedByUserId != null && h.ApprovedByUserId == h.CheckedByUserId)
                                || (h.ManufacturingCheckedByUserId != null && h.ManufacturingCheckedByUserId == h.QcCheckedByUserId) || (h.ApprovedByUserId != null && h.ApprovedByUserId == h.QcCheckedByUserId) || (h.ApprovedByUserId != null && h.ApprovedByUserId == h.ManufacturingCheckedByUserId)))
                        .OrderBy(h => h.Id).Select(h => new { h.Id, h.Code, h.Revision, h.PreparedByUserId, h.CheckedByUserId, h.QcCheckedByUserId, h.ManufacturingCheckedByUserId, h.ApprovedByUserId, h.ModifyDate }));
                    return rows.Where(h => !c.FocusId.HasValue || h.Id == c.FocusId.Value).Select(h =>
                        Hit("PbBomHeader", h.Id, h.Code, $"در BOM {h.Code} بازنگری {h.Revision} یک نفر بیش از یک امضا دارد؛ چهار امضای بررسی و تایید باید از چهار نفر متفاوت باشد.", owner: h.PreparedByUserId, link: "/product-bank")
                            .On(h.ModifyDate).Fact("prepared", h.PreparedByUserId).Fact("checked", h.CheckedByUserId).Fact("qc", h.QcCheckedByUserId).Fact("manufacturing", h.ManufacturingCheckedByUserId).Fact("approved", h.ApprovedByUserId).Fact("revision", h.Revision)).ToList();
                });

            yield return Def("AUD-MC-005", "DOCUMENT", AusSeverity.Major, "بازنگری مدرک مهندسی: تهیه‌کننده همان بازبین یا تاییدکننده است", Iso9, "7.5.2", "Annex 9 EDMS (بازبین و تاییدکننده غیر از تهیه‌کننده)",
                "PcDocumentRevision approved/released (not received from the client) whose reviewer or approver is the preparer", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<PcDocumentRevision>().AsNoTracking())
                        .Where(r => !r.Deleted && !r.ReceivedFromClient && (r.Status == PcDocStatus.Approved || r.Status == PcDocStatus.Released) && r.ModifyDate >= since
                            && (r.ReviewerUserId == r.PreparedByUserId || r.ApproverUserId == r.PreparedByUserId))
                        .OrderBy(r => r.Id).Select(r => new { r.Id, r.DeliverableId, r.Code, r.PreparedByUserId, r.ReviewerUserId, r.ApproverUserId, r.ModifyDate }));
                    return rows.Where(r => !c.FocusId.HasValue || r.Id == c.FocusId.Value).Select(r =>
                        Hit("PcDocumentRevision", r.Id, r.Code, $"بازنگری {r.Code} مدرک {r.DeliverableId}: بازبین یا تاییدکننده همان تهیه‌کننده است.", owner: r.PreparedByUserId, link: "/edms")
                            .On(r.ModifyDate).Fact("deliverable", r.DeliverableId).Fact("preparer", r.PreparedByUserId).Fact("reviewer", r.ReviewerUserId).Fact("approver", r.ApproverUserId)).ToList();
                });

            yield return Def("AUD-MC-006", "AUDIT", AusSeverity.Major, "بستن یافته‌ی ممیزی بدون سه نفر متفاوت (QA، PMO، مدیرعامل) یا با دخالت مسئول", Iso27, "A.5.3", "تصمیم D12 (QA + PMO + مدیرعامل، سه نفر متفاوت)",
                "AusFinding closed where QA, PMO, CEO are not three distinct persons, or one of them is the owner or the actioner", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<AusFinding>().AsNoTracking()).Where(f => !f.Deleted && f.Status == AusFindingStatus.Closed && f.ModifyDate >= since
                            && (f.QaUserId == f.PmoUserId || f.QaUserId == f.CeoUserId || f.PmoUserId == f.CeoUserId || f.QaUserId == f.OwnerUserId || f.PmoUserId == f.OwnerUserId || f.CeoUserId == f.OwnerUserId
                                || f.QaUserId == f.ActionByUserId || f.PmoUserId == f.ActionByUserId || f.CeoUserId == f.ActionByUserId || f.QaUserId == null || f.PmoUserId == null || f.CeoUserId == null))
                        .OrderBy(f => f.Id).Select(f => new { f.Id, f.Number, f.QaUserId, f.PmoUserId, f.CeoUserId, f.OwnerUserId, f.ActionByUserId, f.ClosedAt }));
                    return rows.Where(f => !c.FocusId.HasValue || f.Id == c.FocusId.Value).Select(f =>
                        Hit("AusFinding", f.Id, f.Number, $"یافته‌ی {f.Number} بسته شده ولی زنجیره‌ی بستن آن (QA، PMO، مدیرعامل) سه نفر متفاوت و جدا از مسئول نیست.", link: "/audit/findings/" + f.Id)
                            .On(f.ClosedAt).Fact("qa", f.QaUserId).Fact("pmo", f.PmoUserId).Fact("ceo", f.CeoUserId).Fact("owner", f.OwnerUserId).Fact("actioner", f.ActionByUserId)).ToList();
                });

            yield return Def("AUD-EX-001", "GOVERNANCE", AusSeverity.Minor, "درخواست استثنای کنترل داده بیش از مهلت منتظر تصمیم مانده است", Iso9, "8.5.1", "تصمیم PB8 (استثنا با تصمیم سرپرست)",
                "OpsException pending longer than Audit.T.ExceptionPendingDays", false, async c =>
                {
                    DateTimeOffset limit = c.Now.AddDays(-(double)c.Tol(AusSettingKeys.ExceptionPendingDays, 1));
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<OpsException>().AsNoTracking()).Where(x => !x.Deleted && x.Status == OpsExceptionStatus.Pending && x.RequestedAt < limit).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.RuleCode, x.DocumentNumber, x.RequestedAt, x.RequestedByUserId }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                        Hit("OpsException", x.Id, x.DocumentNumber, $"درخواست استثنای {x.RuleCode} برای سند {x.DocumentNumber} از {P(x.RequestedAt)} منتظر تصمیم است.", "pending", link: "/ops-data/quality?tab=exceptions").Fact("rule", x.RuleCode).Fact("requestedAt", x.RequestedAt)).ToList();
                }, "WHSUPERVISOR");

            yield return Def("AUD-EX-002", "GOVERNANCE", AusSeverity.Minor, "تعهد تکمیل اطلاعات مهندسی (آمادگی پروژه) از مهلت گذشته", "ISO 21502:2020", "7.4", "برنامه PB8 (گیت آمادگی اطلاعات مهندسی)",
                "OpsReadinessObligation open with DueDate before today (Major when the item is Hard)", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<OpsReadinessObligation>().AsNoTracking()).Where(x => !x.Deleted && x.Status == OpsObligationStatus.Open && x.DueDate < today).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.ItemCode, x.Title, x.WorkOrderId, x.Hard, x.DueDate, x.OwnerUnit }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                    {
                        AusHit h = Hit("OpsReadinessObligation", x.Id, x.ItemCode, $"تعهد تکمیل «{x.Title}» ({x.ItemCode}) برای دستور کار {x.WorkOrderId} سررسید {P(x.DueDate)} را گذرانده است.", link: "/ops-data/quality?tab=readiness")
                            .Fact("item", x.ItemCode).Fact("workOrder", x.WorkOrderId).Fact("due", x.DueDate).Fact("hard", x.Hard).Fact("unit", x.OwnerUnit);
                        if (x.Hard) h.Severity = AusSeverity.Major;
                        return h;
                    }).ToList();
                }, "PLANNING");

            yield return Def("AUD-EX-003", "GOVERNANCE", AusSeverity.Minor, "بسامد زیاد استثنای تاییدشده‌ی یک قاعده‌ی کنترل داده", Iso9, "8.5.1", "تصمیم PB8 (کنترل داده)",
                "OpsException approved in the last Audit.T.GateExceptionWindowDays, grouped by rule, count above Audit.T.GateExceptionMax", false, async c =>
                {
                    DateTimeOffset from = c.Now.AddDays(-c.Days(AusSettingKeys.GateExceptionWindow));
                    int max = c.Days(AusSettingKeys.GateExceptionMax);
                    var groups = await c.Uow.Set<OpsException>().AsNoTracking().Where(x => !x.Deleted && x.Status == OpsExceptionStatus.Approved && x.DecidedAt >= from)
                        .GroupBy(x => x.RuleCode).Select(g => new { Rule = g.Key, N = g.Count() }).Where(g => g.N > max).ToListAsync(c.Ct);
                    return groups.OrderBy(g => g.Rule, StringComparer.Ordinal).Select(g =>
                        Hit("OpsRule", 0, g.Rule, $"برای قاعده‌ی {g.Rule} در {c.S(AusSettingKeys.GateExceptionWindow):0} روز گذشته {g.N} استثنا تایید شده (سقف {max}).", g.Rule, link: "/ops-data/quality?tab=exceptions").Fact("rule", g.Rule).Fact("count", g.N).Fact("max", max)).ToList();
                });

            yield return Def("AUD-EX-004", "GOVERNANCE", AusSeverity.Minor, "یافته‌ی کنترل داده‌ی مسدودکننده یا هشدار بیش از مهلت باز مانده است", Iso9, "8.5.1", "تصمیم PB8 (صفحه کیفیت داده)",
                "OpsFinding open longer than Audit.T.ExceptionPendingDays days (0 = one day) (Major when the severity is Block)", false, async c =>
                {
                    DateTimeOffset limit = c.Now.AddDays(-(double)c.Tol(AusSettingKeys.ExceptionPendingDays, 1));
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<OpsFinding>().AsNoTracking()).Where(x => !x.Deleted && x.Status == OpsFindingStatus.Open && x.DetectedAt < limit).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.RuleCode, x.DocumentNumber, x.DocumentType, x.Severity, x.DetectedAt, x.EnteredByUserId, x.Unit }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                    {
                        AusHit h = Hit("OpsFinding", x.Id, x.DocumentNumber, $"یافته‌ی کنترل داده {x.RuleCode} برای {x.DocumentType} {x.DocumentNumber} از {P(x.DetectedAt)} باز مانده است.", owner: x.EnteredByUserId, link: "/ops-data/quality")
                            .Fact("rule", x.RuleCode).Fact("document", x.DocumentNumber).Fact("detectedAt", x.DetectedAt).Fact("unit", x.Unit);
                        if (x.Severity == OpsSeverity.Block) h.Severity = AusSeverity.Major;
                        return h;
                    }).ToList();
                });

            yield return Def("AUD-EX-005", "GOVERNANCE", AusSeverity.Minor, "مورد سخت آمادگی اطلاعات مهندسی با استثنا دور زده شده است", "ISO 21502:2020", "7.4", "برنامه PB8 (استثنای آمادگی با تایید PMO و مدیرعامل)",
                "OpsReadinessException approved or used (every bypass of a Hard readiness item is reported)", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<OpsReadinessException>().AsNoTracking()).Where(x => !x.Deleted && (x.Status == OpsReadinessExceptionStatus.Approved || x.Status == OpsReadinessExceptionStatus.Used) && x.ModifyDate >= since).OrderBy(x => x.Id)
                        .Select(x => new { x.Id, x.WorkOrderId, x.ItemCodes, x.Status, x.RequestedByUserId, x.Justification, x.ModifyDate }));
                    return rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value).Select(x =>
                        Hit("OpsReadinessException", x.Id, x.ItemCodes, $"مورد(های) آمادگی {x.ItemCodes} برای دستور کار {x.WorkOrderId} با استثنا دور زده شده است.", owner: x.RequestedByUserId, link: "/ops-data/quality?tab=readiness")
                            .On(x.ModifyDate).Fact("workOrder", x.WorkOrderId).Fact("items", x.ItemCodes).Fact("status", x.Status).Fact("justification", x.Justification)).ToList();
                });
        }
    }
}
