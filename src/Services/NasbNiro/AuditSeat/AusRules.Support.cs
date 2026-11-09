using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Identity;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;

namespace Services.NasbNiro.AuditSeat
{
    // Rules of the processes PROCUREMENT, AUDIT (management review, internal audit), FINANCE (Sepidar), AI and the audit system itself.
    public static partial class AusCatalog
    {
        private static IEnumerable<AusRuleDef> SupportRules()
        {
            yield return Def("AUD-PR-001", "PROCUREMENT", AusSeverity.Minor, "سفارش خرید صادرشده از تامین‌کننده‌ای که در فهرست تامین‌کنندگان مجاز نیست یا دسته‌اش را پوشش نمی‌دهد", Iso9, "8.4.1", "WI-PO-003 / FO-PO-003/008/009 (فهرست تامین‌کنندگان مجاز)",
                "PurchaseOrder released or later (not cancelled) whose provider has no SupplierProfile of grade A/B/C, is blocked, or whose profile categories do not include the provider category", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(
                        from o in c.Scope(c.Uow.Set<PurchaseOrder>().AsNoTracking())
                        where !o.Deleted && o.Status != PurchaseOrderStatus.Draft && o.Status != PurchaseOrderStatus.Approved && o.Status != PurchaseOrderStatus.Cancelled && o.ModifyDate >= since
                        join sp in c.Uow.Set<SupplierProfile>().AsNoTracking() on o.ProviderId equals sp.ProviderId into spj
                        from sp in spj.DefaultIfEmpty()
                        join p in c.Uow.Set<Provider>().AsNoTracking() on o.ProviderId equals p.Id
                        join pc in c.Uow.Set<ProviderCategory>().AsNoTracking() on p.CategoryId equals pc.Id into pcj
                        from pc in pcj.DefaultIfEmpty()
                        orderby o.Id
                        select new { o.Id, o.Number, o.ProviderId, o.ApprovedByUserId, Grade = (SupplierGrade?)sp.Grade, Blocked = (bool?)sp.Blocked, Categories = sp.Categories, Category = pc.Category, p.Name, o.OrderDate });
                    var hits = new List<AusHit>();
                    foreach (var o in rows)
                    {
                        if (c.FocusId.HasValue && o.Id != c.FocusId.Value) continue;
                        bool notApproved = o.Grade == null || o.Grade == SupplierGrade.NotEvaluated || o.Grade == SupplierGrade.D || o.Blocked == true;
                        bool categoryMismatch = !notApproved && !string.IsNullOrWhiteSpace(o.Categories) && !string.IsNullOrWhiteSpace(o.Category)
                            && !o.Categories.Split(new[] { ',', ';', '،', '؛', '|', '\n' }, StringSplitOptions.RemoveEmptyEntries).Any(t => string.Equals(t.Trim(), o.Category.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (!notApproved && !categoryMismatch) continue;
                        hits.Add(Hit("PurchaseOrder", o.Id, o.Number, notApproved
                                ? $"سفارش خرید {o.Number} از تامین‌کننده‌ی «{o.Name}» صادر شده که در فهرست مجاز نیست (ارزیابی‌نشده، گرید D یا مسدود)."
                                : $"سفارش خرید {o.Number} از تامین‌کننده‌ی «{o.Name}» صادر شده ولی دسته‌ی «{o.Category}» در دسته‌های مجاز او نیست.", notApproved ? "grade" : "category", link: "/procurement/orders")
                            .On(o.OrderDate).Fact("provider", o.ProviderId).Fact("grade", o.Grade).Fact("blocked", o.Blocked).Fact("category", o.Category).Fact("allowedCategories", o.Categories));
                    }
                    return hits;
                });

            yield return Def("AUD-PR-002", "PROCUREMENT", AusSeverity.Minor, "ارزیابی دوره‌ای تامین‌کننده‌ی فعال از موعد گذشته است", Iso9, "8.4.1", "WI-PO-003 (ارزیابی دوره‌ای)",
                "SupplierProfile of a provider with a purchase order in the last year, with NextEvaluation before today or EvaluatedAt older than Audit.T.SupplierEvaluationDays", false, async c =>
                {
                    DateTime today = c.Today, evalLimit = c.Today.AddDays(-c.Days(AusSettingKeys.SupplierEvalDays)), yearAgo = c.Today.AddYears(-1);
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<SupplierProfile>().AsNoTracking()).Where(p => !p.Blocked
                            && c.Uow.Set<PurchaseOrder>().Any(o => o.ProviderId == p.ProviderId && !o.Deleted && o.OrderDate >= yearAgo && o.Status != PurchaseOrderStatus.Cancelled)
                            && ((p.NextEvaluation != null && p.NextEvaluation < today) || p.EvaluatedAt == null || p.EvaluatedAt < evalLimit)).OrderBy(p => p.Id)
                        .Select(p => new { p.Id, p.ProviderId, p.Grade, p.EvaluatedAt, p.NextEvaluation }));
                    return rows.Where(p => !c.FocusId.HasValue || p.Id == c.FocusId.Value).Select(p =>
                        Hit("SupplierProfile", p.Id, p.ProviderId.ToString(), p.EvaluatedAt.HasValue ? $"ارزیابی تامین‌کننده‌ی {p.ProviderId} آخرین بار در {P(p.EvaluatedAt)} انجام شده و موعد ارزیابی دوره‌ای گذشته است." : $"تامین‌کننده‌ی فعال {p.ProviderId} هرگز ارزیابی نشده است.", link: "/procurement/suppliers")
                            .Fact("provider", p.ProviderId).Fact("evaluatedAt", p.EvaluatedAt).Fact("nextEvaluation", p.NextEvaluation).Fact("grade", p.Grade)).ToList();
                });

            yield return Def("AUD-MR-001", "AUDIT", AusSeverity.Minor, "بازنگری مدیریت در فاصله‌ی برنامه‌ریزی‌شده انجام نشده است", Iso9, "9.3", "FO-QA-022 (صورت‌جلسه بازنگری مدیریت)",
                "latest ManagementReview (held/closed) older than Audit.T.ManagementReviewDays, or none", false, async c =>
                {
                    DateTime? last = await c.Uow.Set<ManagementReview>().AsNoTracking().Where(r => !r.Deleted && r.Status != ReviewStatus.Draft).Select(r => (DateTime?)r.ReviewDate).MaxAsync(c.Ct);
                    if (last.HasValue && last.Value >= c.Today.AddDays(-c.Days(AusSettingKeys.ManagementReviewDays))) return new List<AusHit>();
                    return new List<AusHit> { Hit("ManagementReview", 0, null, last.HasValue ? $"آخرین بازنگری مدیریت در {P(last)} برگزار شده و از {c.S(AusSettingKeys.ManagementReviewDays):0} روز گذشته است." : "هنوز هیچ بازنگری مدیریت برگزار نشده است.", "overdue", link: "/quality/management-review").Fact("last", last).Fact("maxDays", c.S(AusSettingKeys.ManagementReviewDays)) };
                });

            yield return Def("AUD-MR-002", "AUDIT", AusSeverity.Minor, "تصمیم بازنگری مدیریت از موعد گذشته و انجام نشده است", Iso9, "9.3.3", "FO-QA-022",
                "ManagementReviewDecision Open with DueDate before today", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<ManagementReviewDecision>().AsNoTracking()).Where(d => d.Status == ReviewDecisionStatus.Open && d.DueDate < today).OrderBy(d => d.Id).Select(d => new { d.Id, d.ReviewId, d.Text, d.DueDate, d.ResponsibleUserId }));
                    return rows.Where(d => !c.FocusId.HasValue || d.Id == c.FocusId.Value).Select(d =>
                        Hit("ManagementReviewDecision", d.Id, d.ReviewId.ToString(), $"تصمیم بازنگری مدیریت «{(d.Text.Length > 80 ? d.Text.Substring(0, 80) : d.Text)}» سررسید {P(d.DueDate)} را گذرانده است.", owner: d.ResponsibleUserId, link: "/quality/management-review").Fact("review", d.ReviewId).Fact("due", d.DueDate)).ToList();
                });

            yield return Def("AUD-IA-001", "AUDIT", AusSeverity.Minor, "ممیزی داخلی برنامه‌ریزی‌شده از موعد گذشته و انجام نشده است", "ISO 19011:2018", "5.4 / 6.2", "برنامه ممیزی داخلی",
                "Audit (quality) Planned and AusAudit Planned/InProgress with PlannedDate before today", false, async c =>
                {
                    DateTime today = c.Today;
                    var hits = new List<AusHit>();
                    var q = await c.BoundedAsync(c.Uow.Set<Audit>().AsNoTracking().Where(a => !a.Deleted && a.Status == AuditStatus.Planned && a.PlannedDate < today).OrderBy(a => a.Id).Select(a => new { a.Id, a.Number, a.AuditedUnit, a.PlannedDate, a.LeadAuditorUserId }));
                    hits.AddRange(q.Where(a => !c.FocusId.HasValue || a.Id == c.FocusId.Value).Select(a => Hit("Audit", a.Id, a.Number, $"ممیزی داخلی {a.Number} ({a.AuditedUnit}) برای {P(a.PlannedDate)} برنامه‌ریزی شده و هنوز انجام نشده است.", "qms", owner: a.LeadAuditorUserId, link: "/quality/audits").Fact("planned", a.PlannedDate)));
                    var m = await c.BoundedAsync(c.Uow.Set<AusAudit>().AsNoTracking().Where(a => !a.Deleted && (a.Status == AusAuditStatus.Planned || a.Status == AusAuditStatus.InProgress) && a.PlannedDate < today).OrderBy(a => a.Id).Select(a => new { a.Id, a.Number, a.ProcessCode, a.PlannedDate, a.LeadAuditorUserId }));
                    hits.AddRange(m.Where(a => !c.FocusId.HasValue || a.Id == c.FocusId.Value).Select(a => Hit("AusAudit", a.Id, a.Number, $"ممیزی فرایند {a.Number} ({AusProcesses.TitleOf(a.ProcessCode)}) برای {P(a.PlannedDate)} برنامه‌ریزی شده و هنوز کامل نشده است.", "process", owner: a.LeadAuditorUserId, link: "/audit/process-audits").Fact("planned", a.PlannedDate)));
                    return hits;
                });

            yield return Def("AUD-IA-002", "AUDIT", AusSeverity.Minor, "یافته‌ی ممیزی داخلی (QMS) از موعد گذشته و CAPA آن بسته نشده است", Iso9, "9.2.2 (d,e)", "FO-QA-017 / برنامه ممیزی داخلی",
                "AuditFinding (quality) with DueDate before today whose Capa is missing or not closed", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<AuditFinding>().AsNoTracking()).Where(f => !f.Deleted && f.DueDate < today && f.Type != AuditFindingType.Opportunity
                            && (f.CapaId == null || c.Uow.Set<Capa>().Any(x => x.Id == f.CapaId && x.Status != CapaStatus.Closed && x.Status != CapaStatus.Cancelled))).OrderBy(f => f.Id)
                        .Select(f => new { f.Id, f.AuditId, f.Clause, f.Type, f.DueDate, f.CapaId }));
                    return rows.Where(f => !c.FocusId.HasValue || f.Id == c.FocusId.Value).Select(f =>
                        Hit("AuditFinding", f.Id, f.Clause, $"یافته‌ی بند {f.Clause} ممیزی {f.AuditId} (موعد {P(f.DueDate)}) هنوز CAPA بسته‌شده ندارد.", link: "/quality/audits").Fact("type", f.Type).Fact("due", f.DueDate).Fact("capa", f.CapaId)).ToList();
                });

            yield return Def("AUD-FIN-001", "FINANCE", AusSeverity.Major, "بسته‌ی مالیاتی/بیمه‌ی واسط سپیدار رد شده یا خطا دارد و حل نشده است", "IAS 2 / Iranian AS 8", "IAS 2.34 (D4)", "تصمیم D4 (فقط داده‌ی مالیات و بیمه)",
                "FnOutboxPackage Rejected or with an Error text, not superseded, with no sent/accepted correction package", false, async c =>
                {
                    var rows = await c.BoundedAsync(c.ScopeBy(c.Uow.Set<FnOutboxPackage>().AsNoTracking(), p => p.Id).Where(p => (p.Status == FnOutboxStatus.Rejected || (p.Error != null && p.Error != "" && p.Status != FnOutboxStatus.Accepted && p.Status != FnOutboxStatus.Superseded))
                            && p.SupersededByPackageId == null && !c.Uow.Set<FnOutboxPackage>().Any(n => n.CorrectsPackageId == p.Id && (n.Status == FnOutboxStatus.Sent || n.Status == FnOutboxStatus.Accepted))).OrderBy(p => p.Id)
                        .Select(p => new { p.Id, p.Number, p.Kind, p.Year, p.Month, p.Status, p.Error, p.CreatedByUserId }));
                    return rows.Where(p => !c.FocusId.HasValue || p.Id == c.FocusId.Value).Select(p =>
                        Hit("FnOutboxPackage", p.Id, p.Number, $"بسته‌ی {p.Kind} {p.Number} (سال {p.Year}) {(p.Status == FnOutboxStatus.Rejected ? "رد شده" : "خطا دارد")} و هنوز بسته‌ی اصلاحی ارسال یا پذیرفته نشده است.", owner: p.CreatedByUserId > 0 ? p.CreatedByUserId : (int?)null, link: "/finance/outbox")
                            .Fact("kind", p.Kind).Fact("status", p.Status).Fact("error", p.Error)).ToList();
                });

                        yield return Def("AUD-AI-001", "AI", AusSeverity.Observation, "پاسخ‌های دستیار هوش مصنوعی در یک روز در راستی‌آزمایی اعداد رد شدند", "ISO/IEC 27001:2022", "A.8.16", "اصل ۱ ماژول هوش مصنوعی (هر عدد از ابزار)",
                "AiMessage (assistant) with Verified = false, grouped by day (one finding per day with the count and the first message ids as evidence)", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var groups = await c.Uow.Set<AiMessage>().AsNoTracking().Where(m => m.Role == AiMessageRole.Assistant && m.Verified == false && m.CreatedAt >= since)
                        .GroupBy(m => new { m.CreatedAt.Year, m.CreatedAt.Month, m.CreatedAt.Day }).Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, N = g.Count(), First = g.Min(m => m.Id), Last = g.Max(m => m.Id) }).OrderBy(g => g.Year).ThenBy(g => g.Month).ThenBy(g => g.Day).Take(c.Max + 1).ToListAsync(c.Ct);
                    if (groups.Count > c.Max) { groups.RemoveAt(groups.Count - 1); c.Continued = true; }
                    return groups.Select(g =>
                    {
                        var day = new DateTime(g.Year, g.Month, g.Day);
                        return Hit("AiVerification", g.Year * 10000 + g.Month * 100 + g.Day, AusEvidence.Day(day), $"{g.N} پاسخ دستیار هوش مصنوعی در روز {AusEvidence.Day(day)} در راستی‌آزمایی اعداد رد شد و متن ثابت جایگزین شد.", link: "/ai")
                            .On(day).Fact("day", day).Fact("count", g.N).Fact("firstMessage", g.First).Fact("lastMessage", g.Last);
                    }).ToList();
                });

            yield return Def("AUD-AI-002", "AI", AusSeverity.Major, "کاربر سرویس صندلی هوش مصنوعی نقشی بیش از نقش‌های تعریف‌شده‌اش دارد", "ISO/IEC 27001:2022", "A.5.3 / A.5.15", "تصمیم D11 (صندلی هوش مصنوعی فقط خوانا)",
                "users named seat.* holding a role that is not in the roles of the seat definition", false, async c =>
                {
                    var hits = new List<AusHit>();
                    List<string> names = AiSeatCatalog.All.Select(s => s.UserName.ToUpper()).ToList();
                    var rows = await (from u in c.Uow.Set<User>().AsNoTracking()
                                      join ur in c.Uow.Set<UserRole>().AsNoTracking() on u.Id equals ur.UserId
                                      join r in c.Uow.Set<Role>().AsNoTracking() on ur.RoleId equals r.Id
                                      where names.Contains(u.UserName.ToUpper())
                                      select new { u.Id, u.UserName, Role = r.Name }).ToListAsync(c.Ct);
                    foreach (var g in rows.GroupBy(x => new { x.Id, x.UserName }).OrderBy(g => g.Key.Id))
                    {
                        AiSeatDefinition def = AiSeatCatalog.All.FirstOrDefault(s => string.Equals(s.UserName, g.Key.UserName, StringComparison.OrdinalIgnoreCase));
                        if (def == null) continue;
                        string[] extra = g.Select(x => x.Role.ToUpperInvariant()).Where(r => !def.Roles.Contains(r, StringComparer.OrdinalIgnoreCase)).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray();
                        if (extra.Length == 0 || (c.FocusId.HasValue && g.Key.Id != c.FocusId.Value)) continue;
                        hits.Add(Hit("User", g.Key.Id, g.Key.UserName, $"کاربر سرویس صندلی {def.Code} ({g.Key.UserName}) نقش(های) {string.Join("، ", extra)} را هم دارد که در تعریف صندلی نیست.", link: "/ai/seats").Fact("seat", def.Code).Fact("extraRoles", string.Join(",", extra)).Fact("allowedRoles", def.RolesText));
                    }
                    return hits;
                });

            yield return Def("AUD-AI-003", "AI", AusSeverity.Minor, "آخرین اجرای یک صندلی هوش مصنوعی ناموفق بوده است", "ISO/IEC 27001:2022", "A.8.16", "برنامه A1 بخش ب (صندلی‌ها)",
                "latest AiSeatRun of a seat has Status Failed", false, async c =>
                {
                    var latest = await c.Uow.Set<AiSeatRun>().AsNoTracking().GroupBy(r => r.SeatId).Select(g => g.Max(r => r.Id)).ToListAsync(c.Ct);
                    if (latest.Count == 0) return new List<AusHit>();
                    var rows = await (from r in c.Uow.Set<AiSeatRun>().AsNoTracking() join s in c.Uow.Set<AiSeat>().AsNoTracking() on r.SeatId equals s.Id
                                      where latest.Contains(r.Id) && r.Status == AiSeatRunStatus.Failed select new { r.Id, r.SeatId, s.Code, r.StartedAt, r.Error }).ToListAsync(c.Ct);
                    return rows.Where(r => !c.FocusId.HasValue || r.SeatId == c.FocusId.Value).Select(r =>
                        Hit("AiSeat", r.SeatId, r.Code, $"آخرین اجرای صندلی {r.Code} ({P(r.StartedAt)}) ناموفق بود.", r.Code, link: "/ai/seats").Fact("run", r.Id).Fact("seat", r.Code).Fact("error", r.Error)).ToList();
                });

            yield return Def("AUD-SYS-001", "AUDIT", AusSeverity.Major, "اجرای یک قاعده‌ی ممیزی ناقص ماند (خطا یا پایان زمان بودجه)", "ISO 19011:2018", "5.5 / 6.5", "برنامه ممیزی داخلی (کامل بودن پوشش)",
                "AusRuleState of another rule with LastStatus error or timeout (a pass continued from its checkpoint is not a failure)", false, async c =>
                {
                    var rows = await c.BoundedAsync(c.Uow.Set<AusRuleState>().AsNoTracking().Where(s => !s.Deleted && s.RuleCode != "AUD-SYS-001" && s.LastStatus != null && s.LastStatus != "ok" && s.LastStatus != "continued" && s.LastStatus != "disabled").OrderBy(s => s.Id).Select(s => new { s.Id, s.RuleCode, s.LastStatus, s.LastError, s.LastRunAt }));
                    return rows.Select(s => Hit("AusRuleState", s.Id, s.RuleCode, $"قاعده‌ی {s.RuleCode} در آخرین اجرا ({P(s.LastRunAt)}) ناقص ماند: {s.LastStatus}.", s.RuleCode, link: "/audit/runs").Fact("rule", s.RuleCode).Fact("status", s.LastStatus).Fact("error", s.LastError)).ToList();
                });

            yield return Def("AUD-SYS-002", "AUDIT", AusSeverity.Minor, "یافته‌ی ممیزی از مهلت اقدام گذشته و هنوز باز است", "ISO 19011:2018", "6.7", "تصمیم D12 (هیچ یافته‌ای رها نمی‌شود)",
                "AusFinding not closed/voided with DueDate before today and not yet escalated (the run escalates the SAME finding once: EscalatedAt, an event and one consolidated alert; it opens no second finding)", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(c.Scope(c.Uow.Set<AusFinding>().AsNoTracking()).Where(f => !f.Deleted && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided && f.DueDate < today && f.RuleCode != "AUD-SYS-002" && f.EscalatedAt == null).OrderBy(f => f.Id)
                        .Select(f => new { f.Id, f.Number, f.RuleCode, f.Severity, f.DueDate, f.OwnerUserId }));
                    return rows.Where(f => !c.FocusId.HasValue || f.Id == c.FocusId.Value).Select(f =>
                        Hit("AusFinding", f.Id, f.Number, $"یافته‌ی {f.Number} ({AusRules.SeverityText(f.Severity)}، قاعده‌ی {f.RuleCode}) از مهلت {P(f.DueDate)} گذشته و بسته نشده است.", "overdue", owner: f.OwnerUserId, link: "/audit/findings/" + f.Id)
                            .Fact("rule", f.RuleCode).Fact("severity", f.Severity).Fact("due", f.DueDate)).ToList();
                });

            yield return Def("AUD-SYS-003", "AUDIT", AusSeverity.Major, "درهم‌ریختگی گواهی‌ی یافته یا گزارش ممیزی (هش با محتوا نمی‌خواند)", "ISO 19011:2018", "6.6 / 6.7", "تصمیم D12 (سابقه‌ی ممیزی دست‌نخورده می‌ماند)",
                "AusFinding whose SHA-256 of EvidenceJson differs from EvidenceSha256, and AusReport whose SHA-256 of title|period|content|narrative differs from Sha256 (paged by id, the hash is computed in memory; a re-check restricts to the object)", false, async c =>
                {
                    var hits = new List<AusHit>();
                    hits.AddRange(await c.ScanAsync(c.Uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.EvidenceSha256 != null), async fs =>
                    {
                        var part = new List<AusHit>();
                        foreach (var f in fs)
                            if (!string.Equals(AusEvidence.Sha256Hex(f.EvidenceJson ?? ""), f.EvidenceSha256, StringComparison.Ordinal))
                                part.Add(Hit("AusFinding", f.Id, f.Number, $"هش گواهی‌ی یافته‌ی {f.Number} با محتوای آن نمی‌خواند.", "evidence", link: "/audit/findings/" + f.Id).Fact("stored", f.EvidenceSha256));
                        return await Task.FromResult(part);
                    }, 100));
                    if (c.Continued) return hits;
                    var reps = await c.Uow.Set<AusReport>().AsNoTracking().Where(r => !r.Deleted && (!c.FocusId.HasValue || r.Id == c.FocusId.Value)).OrderBy(r => r.Id)
                        .Select(r => new { r.Id, r.Title, r.Kind, r.PeriodFrom, r.PeriodTo, r.ContentJson, r.Narrative, r.Sha256 }).Take(c.Max + 1).ToListAsync(c.Ct);
                    foreach (var r in reps.Take(c.Max))
                    {
                        string h = AusEvidence.Sha256Hex(r.Title + "|" + r.PeriodFrom.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "|" + r.PeriodTo.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "|" + r.ContentJson + "|" + r.Narrative);
                        if (!string.Equals(h, r.Sha256, StringComparison.Ordinal))
                            hits.Add(Hit("AusReport", r.Id, r.Title, $"هش گزارش «{r.Title}» با محتوا یا متن آن نمی‌خواند.", "report", link: "/audit/reports/" + r.Id).Fact("stored", r.Sha256));
                    }
                    return hits;
                });
        }
    }
}
