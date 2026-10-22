using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // Controls, audit trail and housekeeping (F3 section 9): audit queries, period-close checklist, voucher attachments,
    // approval thresholds, template vouchers, voucher copy and mass entry.
    public partial class GlService
    {
        private static readonly string[] SettingKeys = { FinanceSeed.SettingCloseEnforce, FinanceSeed.SettingParallelMonths, FinanceSeed.SettingSepidarExport, FinanceSeed.SettingFxRateType, FinanceSeed.SettingFxAutoReverse };

        // ------------------------------------------------------------------------------------------ audit trail

        public async Task<ResponseDto<PagedList<AuditLogDto>>> AuditAsync(AuditQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.AuditReaders)) return Bad<PagedList<AuditLogDto>>(NotAllowed);
            query ??= new AuditQuery();
            IQueryable<FnAuditLog> q = _uow.Set<FnAuditLog>().AsNoTracking();
            string type = Clean(query.EntityType);
            if (type != null) q = q.Where(a => a.EntityType == type);
            if (query.EntityId.HasValue) q = q.Where(a => a.EntityId == query.EntityId.Value);
            if (query.UserId.HasValue) q = q.Where(a => a.UserId == query.UserId.Value);
            string action = Clean(query.Action);
            if (action != null) q = q.Where(a => a.Action == action);
            if (query.From.HasValue) { DateTimeOffset f = new DateTimeOffset(query.From.Value.Date); q = q.Where(a => a.At >= f); }
            if (query.To.HasValue) { DateTimeOffset t = new DateTimeOffset(query.To.Value.Date.AddDays(1)); q = q.Where(a => a.At < t); }
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnAuditLog> rows = await q.OrderByDescending(a => a.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => (int?)r.UserId), ct);
            var dtos = rows.Select(a => new AuditLogDto
            {
                Id = a.Id, EntityType = a.EntityType, EntityId = a.EntityId, Action = a.Action, Summary = a.Summary, BeforeJson = a.BeforeJson, AfterJson = a.AfterJson,
                UserId = a.UserId, UserName = a.UserId == 0 ? "سیستم" : names.Name(a.UserId), At = a.At, Ip = a.Ip,
            }).ToList();
            return ResponseDtoHelper.Ok(F2Page.Make(dtos, total, number, size));
        }

        // ------------------------------------------------------------------------------------------ settings

        public async Task<ResponseDto<List<FinanceSettingDto>>> GetSettingsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FinanceSettingDto>>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            await FinanceSeed.EnsureSettingsAsync(_uow, ct);
            var list = await _uow.Set<FnSetting>().AsNoTracking().Where(s => SettingKeys.Contains(s.Key)).OrderBy(s => s.Key)
                .Select(s => new FinanceSettingDto { Key = s.Key, Value = s.Value, Title = s.Title }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<FinanceSettingDto>> SaveSettingAsync(SaveFinanceSettingDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FinanceSettingDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || Clean(dto.Key) == null || !SettingKeys.Contains(dto.Key.Trim())) return Bad<FinanceSettingDto>("کلید تنظیم نامعتبر است.");
            string value = Clean(dto.Value);
            if (value == null) return Bad<FinanceSettingDto>("مقدار را وارد کنید.");
            string key = dto.Key.Trim();
            if (key == FinanceSeed.SettingParallelMonths)
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int months) || months < 1 || months > 12) return Bad<FinanceSettingDto>("تعداد ماه باید بین ۱ تا ۱۲ باشد.");
            }
            else if (value != "0" && value != "1") return Bad<FinanceSettingDto>("مقدار باید ۰ یا ۱ باشد.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            await FinanceSeed.EnsureSettingsAsync(_uow, ct);
            FnSetting s = await _uow.Set<FnSetting>().FirstAsync(x => x.Key == key, ct);
            s.Value = value;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new FinanceSettingDto { Key = s.Key, Value = s.Value, Title = s.Title });
        }

        private async Task<string> SettingAsync(string key, string fallback, CancellationToken ct)
        {
            string v = await _uow.Set<FnSetting>().AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
            return v ?? fallback;
        }

        // ------------------------------------------------------------------------------------------ period-close checklist

        private async Task<List<CloseItemDto>> ComputeChecklistAsync(FnPeriod p, int entityId, CancellationToken ct)
        {
            DateTime start = p.StartDate, end = p.EndDate;
            var items = new List<CloseItemDto>();
            void Add(string key, string title, int count, string detail, bool applicable = true) =>
                items.Add(new CloseItemDto { Key = key, Title = title, Count = count, Detail = detail, Status = !applicable ? "NotApplicable" : count > 0 ? "Red" : "Green" });

            AccountingEventStatus[] unposted = { AccountingEventStatus.Pending, AccountingEventStatus.Exported, AccountingEventStatus.Failed, AccountingEventStatus.Sent };
            int events = await _uow.Set<AccountingEvent>().CountAsync(e => unposted.Contains(e.Status) && e.EventDate >= start && e.EventDate <= end
                && (e.LegalEntityId == entityId || (entityId == FinanceSeed.MainEntityId && e.LegalEntityId == 0)), ct);
            Add("UnpostedEvents", "رویدادهای ثبت‌نشده در دفتر کل", events, events == 0 ? null : $"{events} رویداد هنوز به سند تبدیل نشده است.");

            int nonFinal = await _uow.Set<FnVoucher>().CountAsync(v => v.FiscalYearId == p.FiscalYearId && v.Date >= start && v.Date <= end && v.Status != FnVoucherStatus.Final, ct);
            Add("NonFinalVouchers", "اسناد پیش‌نویس یا کنترل‌شده", nonFinal, nonFinal == 0 ? null : $"{nonFinal} سند قطعی نشده است.");

            if (entityId == FinanceSeed.MainEntityId)
            {
                List<int> banks = await _uow.Set<FnBankStatement>().AsNoTracking().Where(s => s.FromDate <= end && s.ToDate >= start).Select(s => s.BankAccountId).Distinct().ToListAsync(ct);
                int unreconciled = 0;
                foreach (int bankId in banks)
                    if (!await _uow.Set<FnBankReconciliation>().AnyAsync(r => r.BankAccountId == bankId && r.Status == 1 && r.AsOfDate >= start, ct)) unreconciled++;
                Add("BankReconciliations", "مغایرت‌گیری بانکی تاییدشده", unreconciled, unreconciled == 0 ? null : $"{unreconciled} حساب بانکی مغایرت‌گیری تاییدشده ندارد.");

                bool assets = await _uow.Set<FnAsset>().AnyAsync(a => (a.Status == FnAssetStatus.InUse || a.Status == FnAssetStatus.Idle) && (a.InUseDate == null || a.InUseDate <= end), ct);
                bool depreciated = await _uow.Set<FnDepreciationRun>().AnyAsync(r => r.PeriodId == p.Id && r.Status == FnRunStatus.Approved, ct);
                Add("Depreciation", "استهلاک ماه تاییدشده", assets && !depreciated ? 1 : 0, assets && !depreciated ? "اجرای استهلاک این دوره تایید نشده است." : null, assets);

                int pYear = PersianCal.Year(start), pMonth = PersianCal.Month(start);
                bool staff = await _uow.Set<FnEmployee>().AnyAsync(e => e.IsActive, ct);
                bool paid = await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == pYear && r.Month == pMonth && (r.Status == FnPayrollStatus.Posted || r.Status == FnPayrollStatus.Paid), ct);
                Add("Payroll", "حقوق ماه ثبت‌شده", staff && !paid ? 1 : 0, staff && !paid ? "فهرست حقوق این ماه در دفتر ثبت نشده است." : null, staff);

                var recon = await _reports.ReconcileAsync(7, ct);
                int reconBad = recon.Success ? recon.Data.Rows.Count(r => !r.Ok) : 0;
                Add("InventoryReconciliation", "تطبیق موجودی، کالای دریافتی فاکتورنشده، پرداختنی‌ها و بانک", reconBad, reconBad == 0 ? null : $"{reconBad} مورد مغایرت دارد.");

                int month = PersianCal.Month(end);
                if (month % 3 == 0)
                {
                    int season = PersianCal.Season(month);
                    bool prepared = await _uow.Set<FnOutboxPackage>().AnyAsync(x => x.Kind == FnOutboxKind.VatSeason && x.Year == pYear && x.Season == season && x.Status != FnOutboxStatus.Superseded, ct);
                    Add("VatSeason", "اظهارنامه ارزش افزوده فصل آماده شده", prepared ? 0 : 1, prepared ? null : "بسته داده ارزش افزوده این فصل ساخته نشده است.");
                }
            }

            bool fxActive = await _uow.Set<FnVoucherLine>().AnyAsync(l => l.CurrencyCode != null && l.Account.IsMonetaryForeign && l.Voucher.Date <= end && l.Voucher.FiscalYear.LegalEntityId == entityId, ct);
            bool revalued = await _uow.Set<FnFxRevaluation>().AnyAsync(r => r.PeriodId == p.Id && r.LegalEntityId == entityId, ct);
            Add("FxRevaluation", "تسعیر ارز پایان دوره", fxActive && !revalued ? 1 : 0, fxActive && !revalued ? "تسعیر ارز این دوره انجام نشده است." : null, fxActive);

            var activeDeferrals = await _uow.Set<FnDeferral>().AsNoTracking().Where(d => d.Status == FnDeferralStatus.Active && d.LegalEntityId == entityId && d.StartDate <= end && d.EndDate >= start).Select(d => d.Id).ToListAsync(ct);
            int deferralOpen = 0;
            foreach (int did in activeDeferrals)
                if (!await _uow.Set<FnDeferralEntry>().AnyAsync(e => e.DeferralId == did && e.PeriodId == p.Id && e.Status == 1, ct)) deferralOpen++;
            Add("Deferrals", "استهلاک پیش‌پرداخت‌ها و درآمدهای انتقالی تاییدشده", deferralOpen, deferralOpen == 0 ? null : $"{deferralOpen} برنامه استهلاک برای این دوره تایید نشده است.", activeDeferrals.Count > 0);

            bool overheadSetup = await _uow.Set<FnOverheadRate>().AnyAsync(ct) || await _uow.Set<FnAllocationKey>().AnyAsync(ct);
            bool allocated = await _uow.Set<FnOverheadAllocation>().AnyAsync(a => a.PeriodId == p.Id, ct);
            Add("OverheadAllocation", "تسهیم و جذب سربار", overheadSetup && !allocated ? 1 : 0, overheadSetup && !allocated ? "تسهیم و جذب سربار این دوره انجام نشده است." : null, overheadSetup);

            int icOpen = 0;
            List<FnIntercompanyPost> posts = await _uow.Set<FnIntercompanyPost>().AsNoTracking().Where(x => x.Date >= start && x.Date <= end).ToListAsync(ct);
            foreach (FnIntercompanyPost post in posts)
            {
                int made = await _uow.Set<FnVoucher>().CountAsync(v => v.AccountingEventId == post.EventAId || v.AccountingEventId == post.EventBId, ct);
                if (made != 2) icOpen++;
            }
            Add("Intercompany", "تراکنش‌های بین‌شرکتی در هر دو واحد ثبت شده", icOpen, icOpen == 0 ? null : $"{icOpen} تراکنش بین‌شرکتی در یکی از دو واحد ثبت نشده است.", posts.Count > 0);
            return items;
        }

        private async Task<CloseChecklistDto> BuildChecklistAsync(FnPeriod p, CancellationToken ct)
        {
            int entityId = p.FiscalYear?.LegalEntityId ?? await _uow.Set<FnFiscalYear>().Where(y => y.Id == p.FiscalYearId).Select(y => y.LegalEntityId).FirstAsync(ct);
            List<CloseItemDto> items = await ComputeChecklistAsync(p, entityId, ct);
            FnCloseChecklist ov = await _uow.Set<FnCloseChecklist>().AsNoTracking().FirstOrDefaultAsync(x => x.PeriodId == p.Id, ct);
            bool enforced = await SettingAsync(FinanceSeed.SettingCloseEnforce, "0", ct) == "1";
            int red = items.Count(i => i.Status == "Red");
            return new CloseChecklistDto
            {
                PeriodId = p.Id, PeriodTitle = p.Title, LegalEntityId = entityId, Items = items, RedCount = red, AllGreen = red == 0,
                Overridden = ov != null, OverrideReason = ov?.OverrideReason, OverriddenByUserId = ov?.OverriddenByUserId, OverriddenAt = ov?.OverriddenAt,
                Enforced = enforced, CanLock = !enforced || red == 0 || ov != null,
            };
        }

        public async Task<ResponseDto<CloseChecklistDto>> GetChecklistAsync(int periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<CloseChecklistDto>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            FnPeriod p = await _uow.Set<FnPeriod>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId, ct);
            if (p == null) return ResponseDtoHelper.NotFound<CloseChecklistDto>("دوره پیدا نشد.");
            return ResponseDtoHelper.Ok(await BuildChecklistAsync(p, ct));
        }

        public async Task<ResponseDto<CloseChecklistDto>> OverrideChecklistAsync(int periodId, ChecklistOverrideDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<CloseChecklistDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || Clean(dto.Reason) == null) return Bad<CloseChecklistDto>("علت عبور از فهرست کنترل را بنویسید.");
            FnPeriod p = await _uow.Set<FnPeriod>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId, ct);
            if (p == null) return ResponseDtoHelper.NotFound<CloseChecklistDto>("دوره پیدا نشد.");
            CloseChecklistDto current = await BuildChecklistAsync(p, ct);
            string reds = string.Join(" | ", current.Items.Where(i => i.Status == "Red").Select(i => i.Key));
            FnCloseChecklist ov = await _uow.Set<FnCloseChecklist>().FirstOrDefaultAsync(x => x.PeriodId == periodId, ct);
            if (ov == null) { ov = new FnCloseChecklist { PeriodId = periodId }; _uow.Set<FnCloseChecklist>().Add(ov); }
            ov.OverrideReason = Clean(dto.Reason);
            ov.OverriddenByUserId = Me;
            ov.OverriddenAt = DateTimeOffset.Now;
            ov.RedItems = reds.Length > 2000 ? reds.Substring(0, 2000) : reds;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await BuildChecklistAsync(p, ct));
        }

        /// <summary>Null when the period may be locked; otherwise the reason (enforced checklist with red items and no override).</summary>
        private async Task<string> LockGateAsync(FnPeriod p, CancellationToken ct)
        {
            if (await SettingAsync(FinanceSeed.SettingCloseEnforce, "0", ct) != "1") return null;
            CloseChecklistDto list = await BuildChecklistAsync(p, ct);
            if (list.AllGreen || list.Overridden) return null;
            return $"فهرست کنترل بستن دوره {list.RedCount} مورد قرمز دارد ({string.Join("، ", list.Items.Where(i => i.Status == "Red").Select(i => i.Title))}). مدیر مالی باید با ذکر علت تایید ویژه بدهد.";
        }

        // ------------------------------------------------------------------------------------------ attachments, rules, thresholds

        public async Task<ResponseDto<List<VoucherAttachmentDto>>> GetAttachmentsAsync(int voucherId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<VoucherAttachmentDto>>(NotAllowed);
            var list = await _uow.Set<FnVoucherAttachment>().AsNoTracking().Where(a => a.VoucherId == voucherId).OrderBy(a => a.Id)
                .Select(a => new VoucherAttachmentDto { Id = a.Id, VoucherId = a.VoucherId, DocumentId = a.DocumentId, Title = a.Title, AddedByUserId = a.AddedByUserId, AddedAt = a.AddedAt }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<VoucherAttachmentDto>> AddAttachmentAsync(AddAttachmentDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<VoucherAttachmentDto>(NotAllowed);
            if (dto == null || dto.DocumentId == Guid.Empty) return Bad<VoucherAttachmentDto>("شناسه سند پیوست را وارد کنید.");
            FnVoucher v = await _uow.Set<FnVoucher>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.VoucherId, ct);
            if (v == null) return ResponseDtoHelper.NotFound<VoucherAttachmentDto>("سند پیدا نشد.");
            var a = new FnVoucherAttachment { VoucherId = v.Id, DocumentId = dto.DocumentId, Title = Clean(dto.Title), AddedByUserId = Me, AddedAt = DateTimeOffset.Now };
            _uow.Set<FnVoucherAttachment>().Add(a);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new VoucherAttachmentDto { Id = a.Id, VoucherId = a.VoucherId, DocumentId = a.DocumentId, Title = a.Title, AddedByUserId = a.AddedByUserId, AddedAt = a.AddedAt });
        }

        public async Task<ResponseDto<bool>> RemoveAttachmentAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<bool>(NotAllowed);
            FnVoucherAttachment a = await _uow.Set<FnVoucherAttachment>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a == null) return ResponseDtoHelper.NotFound<bool>("پیوست پیدا نشد.");
            FnVoucherStatus status = await _uow.Set<FnVoucher>().Where(v => v.Id == a.VoucherId).Select(v => v.Status).FirstAsync(ct);
            if (status == FnVoucherStatus.Final) return Bad<bool>("پیوست سند قطعی حذف نمی‌شود.");
            _uow.Set<FnVoucherAttachment>().Remove(a);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<List<AttachmentRuleDto>>> GetRulesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<AttachmentRuleDto>>(NotAllowed);
            var list = await _uow.Set<FnAttachmentRule>().AsNoTracking().OrderBy(r => r.Id)
                .Select(r => new AttachmentRuleDto { Id = r.Id, AccountId = r.AccountId, AccountCode = r.Account.Code, VoucherType = r.VoucherType, Note = r.Note, IsActive = r.IsActive }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<AttachmentRuleDto>> SaveRuleAsync(SaveAttachmentRuleDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<AttachmentRuleDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null) return Bad<AttachmentRuleDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnAccount account = null;
            if (Clean(dto.AccountCode) != null)
            {
                string code = Digits(dto.AccountCode.Trim());
                account = await _uow.Set<FnAccount>().FirstOrDefaultAsync(a => a.Code == code, ct);
                if (account == null) return Bad<AttachmentRuleDto>("حساب پیدا نشد.");
            }
            if (account == null && !dto.VoucherType.HasValue) return Bad<AttachmentRuleDto>("حساب یا نوع سند را مشخص کنید.");
            FnAttachmentRule r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<FnAttachmentRule>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (r == null) return ResponseDtoHelper.NotFound<AttachmentRuleDto>("قاعده پیدا نشد.");
            }
            else { r = new FnAttachmentRule(); _uow.Set<FnAttachmentRule>().Add(r); }
            r.AccountId = account?.Id; r.VoucherType = dto.VoucherType; r.Note = Clean(dto.Note); r.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new AttachmentRuleDto { Id = r.Id, AccountId = r.AccountId, AccountCode = account?.Code, VoucherType = r.VoucherType, Note = r.Note, IsActive = r.IsActive });
        }

        public async Task<ResponseDto<List<ApprovalThresholdDto>>> GetThresholdsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<ApprovalThresholdDto>>(NotAllowed);
            var list = await _uow.Set<FnApprovalThreshold>().AsNoTracking().OrderBy(t => t.MinAmount)
                .Select(t => new ApprovalThresholdDto { Id = t.Id, MinAmount = t.MinAmount, Note = t.Note, IsActive = t.IsActive }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<ApprovalThresholdDto>> SaveThresholdAsync(SaveApprovalThresholdDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<ApprovalThresholdDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || R(dto.MinAmount) <= 0) return Bad<ApprovalThresholdDto>("حد مبلغ باید بیشتر از صفر باشد.");
            FnApprovalThreshold t;
            if (dto.Id.HasValue)
            {
                t = await _uow.Set<FnApprovalThreshold>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (t == null) return ResponseDtoHelper.NotFound<ApprovalThresholdDto>("حد تایید پیدا نشد.");
            }
            else { t = new FnApprovalThreshold(); _uow.Set<FnApprovalThreshold>().Add(t); }
            t.MinAmount = R(dto.MinAmount); t.Note = Clean(dto.Note); t.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new ApprovalThresholdDto { Id = t.Id, MinAmount = t.MinAmount, Note = t.Note, IsActive = t.IsActive });
        }

        /// <summary>Per voucher: the reason it cannot be checked (attachment rule) or finalised (attachment rule, CEO approval above the threshold). Vouchers without a problem are absent.</summary>
        private async Task<Dictionary<int, string>> VoucherGateAsync(List<int> ids, bool finalize, CancellationToken ct)
        {
            var result = new Dictionary<int, string>();
            if (ids.Count == 0) return result;
            List<FnAttachmentRule> rules = await _uow.Set<FnAttachmentRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
            decimal? threshold = null;
            if (finalize)
            {
                List<decimal> mins = await _uow.Set<FnApprovalThreshold>().AsNoTracking().Where(t => t.IsActive).Select(t => t.MinAmount).ToListAsync(ct);
                if (mins.Count > 0) threshold = mins.Min();
            }
            if (rules.Count == 0 && !threshold.HasValue) return result;

            var vouchers = await _uow.Set<FnVoucher>().AsNoTracking().Where(v => ids.Contains(v.Id))
                .Select(v => new { v.Id, v.Type, v.TotalAmount, v.CeoApprovedByUserId, v.IsImported }).ToListAsync(ct);
            Dictionary<int, int> attachments = await _uow.Set<FnVoucherAttachment>().AsNoTracking().Where(a => ids.Contains(a.VoucherId))
                .GroupBy(a => a.VoucherId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            List<int> ruleAccounts = rules.Where(r => r.AccountId.HasValue).Select(r => r.AccountId.Value).Distinct().ToList();
            var touched = ruleAccounts.Count == 0 ? new List<(int VoucherId, int AccountId)>()
                : (await _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => ids.Contains(l.VoucherId) && ruleAccounts.Contains(l.AccountId))
                    .Select(l => new { l.VoucherId, l.AccountId }).Distinct().ToListAsync(ct)).Select(x => (x.VoucherId, x.AccountId)).ToList();

            foreach (var v in vouchers)
            {
                if (v.IsImported || v.Type == FnVoucherType.Opening || v.Type == FnVoucherType.Closing || v.Type == FnVoucherType.YearEndTemporaryClose) continue;
                bool needs = rules.Any(r => (r.VoucherType.HasValue && r.VoucherType.Value == v.Type) || (r.AccountId.HasValue && touched.Any(t => t.VoucherId == v.Id && t.AccountId == r.AccountId.Value)));
                if (needs && !attachments.ContainsKey(v.Id)) { result[v.Id] = "پیوست الزامی است؛ قاعده پیوست اجباری برای این سند اعمال می‌شود."; continue; }
                if (finalize && threshold.HasValue && v.TotalAmount >= threshold.Value && !v.CeoApprovedByUserId.HasValue)
                    result[v.Id] = "مبلغ سند به حد نیازمند تایید مدیرعامل رسیده است؛ ابتدا مدیرعامل تایید کند.";
            }
            return result;
        }

        private async Task<ResponseDto<VoucherDto>> CeoApproveAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Ceo)) return Bad<VoucherDto>(NotAllowed + " (مدیرعامل)");
            FnVoucher v = await _uow.Set<FnVoucher>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v == null) return ResponseDtoHelper.NotFound<VoucherDto>("سند پیدا نشد.");
            if (v.Status == FnVoucherStatus.Final) return Bad<VoucherDto>("سند قطعی شده است.");
            if (v.CeoApprovedByUserId.HasValue) return Bad<VoucherDto>("این سند قبلاً تایید مدیرعامل را گرفته است.");
            v.CeoApprovedByUserId = Me;
            v.CeoApprovedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return await GetVoucherAsync(v.Id, ct);
        }

        // ------------------------------------------------------------------------------------------ copy

        public async Task<ResponseDto<VoucherDto>> CopyVoucherAsync(VoucherIdDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<VoucherDto>(NotAllowed);
            if (dto == null) return Bad<VoucherDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnVoucher src = await _uow.Set<FnVoucher>().Include(v => v.Lines).Include(v => v.FiscalYear).FirstOrDefaultAsync(v => v.Id == dto.Id, ct);
            if (src == null) return ResponseDtoHelper.NotFound<VoucherDto>("سند پیدا نشد.");
            if (src.IsImported) return Bad<VoucherDto>("سند واردشده از سپیدار کپی نمی‌شود.");
            if (src.Type == FnVoucherType.Opening || src.Type == FnVoucherType.Closing || src.Type == FnVoucherType.YearEndTemporaryClose) return Bad<VoucherDto>("سند افتتاحیه و اختتامیه کپی نمی‌شود.");
            DateTime date = (dto.Date ?? DateTime.Today).Date;
            var check = await CheckDateAsync(date, src.FiscalYear.LegalEntityId, ct);
            if (check.Error != null) return Bad<VoucherDto>(check.Error);
            var copy = new FnVoucher
            {
                FiscalYearId = check.Year.Id, Date = date, Type = FnVoucherType.Normal, Status = FnVoucherStatus.Draft,
                Description = ("کپی سند " + (src.Number?.ToString(CultureInfo.InvariantCulture) ?? ("موقت " + src.TemporaryNumber)) + (Clean(src.Description) != null ? " - " + src.Description : "")),
                CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, TotalAmount = src.TotalAmount,
                TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, check.Year.Year, ct),
                Lines = src.Lines.OrderBy(l => l.RowNumber).Select(l => new FnVoucherLine
                {
                    RowNumber = l.RowNumber, AccountId = l.AccountId, Detail1Id = l.Detail1Id, Detail2Id = l.Detail2Id, Debit = l.Debit, Credit = l.Credit,
                    CurrencyCode = l.CurrencyCode, CurrencyAmount = l.CurrencyAmount, Rate = l.Rate, Quantity = l.Quantity, Description = l.Description,
                    WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, CostCenterId = l.CostCenterId,
                }).ToList(),
            };
            if (copy.Description.Length > 1000) copy.Description = copy.Description.Substring(0, 1000);
            _uow.Set<FnVoucher>().Add(copy);
            await _uow.SaveChangesAsync(ct);
            return await GetVoucherAsync(copy.Id, ct);
        }

        // ------------------------------------------------------------------------------------------ template vouchers

        private async Task<VoucherTemplateDto> TemplateDtoAsync(FnVoucherTemplate t, CancellationToken ct)
        {
            List<int> accountIds = t.Lines.Select(l => l.AccountId).Distinct().ToList();
            Dictionary<int, string> codes = await _uow.Set<FnAccount>().AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            return new VoucherTemplateDto
            {
                Id = t.Id, Title = t.Title, Description = t.Description, DayOfMonth = t.DayOfMonth, IsActive = t.IsActive,
                Lines = t.Lines.OrderBy(l => l.RowNumber).Select(l => new VoucherTemplateLineDto
                {
                    RowNumber = l.RowNumber, AccountCode = codes.TryGetValue(l.AccountId, out string c) ? c : null, Detail1Id = l.Detail1Id, Detail2Id = l.Detail2Id,
                    Debit = l.Debit, Credit = l.Credit, Description = l.Description,
                }).ToList(),
            };
        }

        public async Task<ResponseDto<List<VoucherTemplateDto>>> GetTemplatesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<VoucherTemplateDto>>(NotAllowed);
            List<FnVoucherTemplate> list = await _uow.Set<FnVoucherTemplate>().AsNoTracking().Include(t => t.Lines).OrderBy(t => t.Id).ToListAsync(ct);
            var dtos = new List<VoucherTemplateDto>();
            foreach (FnVoucherTemplate t in list) dtos.Add(await TemplateDtoAsync(t, ct));
            return ResponseDtoHelper.Ok(dtos);
        }

        public async Task<ResponseDto<VoucherTemplateDto>> SaveTemplateAsync(SaveVoucherTemplateDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<VoucherTemplateDto>(NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<VoucherTemplateDto>("عنوان سند الگو را وارد کنید.");
            if (dto.DayOfMonth < 1 || dto.DayOfMonth > 31) return Bad<VoucherTemplateDto>("روز ماه باید بین ۱ تا ۳۱ باشد.");
            var asVoucher = (dto.Lines ?? new List<VoucherTemplateLineDto>()).Select(l => new SaveVoucherLineDto
            {
                AccountCode = l.AccountCode, Detail1Id = l.Detail1Id, Detail2Id = l.Detail2Id, Debit = l.Debit, Credit = l.Credit, Description = l.Description,
            }).ToList();
            var built = await BuildLinesAsync(asVoucher, true, ct);
            if (built.Error != null) return Bad<VoucherTemplateDto>(built.Error);
            FnVoucherTemplate t;
            if (dto.Id.HasValue)
            {
                t = await _uow.Set<FnVoucherTemplate>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (t == null) return ResponseDtoHelper.NotFound<VoucherTemplateDto>("سند الگو پیدا نشد.");
                _uow.Set<FnVoucherTemplateLine>().RemoveRange(t.Lines);
                t.Lines = new List<FnVoucherTemplateLine>();
            }
            else
            {
                t = new FnVoucherTemplate { CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Lines = new List<FnVoucherTemplateLine>() };
                _uow.Set<FnVoucherTemplate>().Add(t);
            }
            t.Title = Clean(dto.Title); t.Description = Clean(dto.Description); t.DayOfMonth = dto.DayOfMonth; t.IsActive = dto.IsActive;
            foreach (FnVoucherLine l in built.Lines)
                t.Lines.Add(new FnVoucherTemplateLine { RowNumber = l.RowNumber, AccountId = l.AccountId, Detail1Id = l.Detail1Id, Detail2Id = l.Detail2Id, Debit = l.Debit, Credit = l.Credit, Description = l.Description });
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await TemplateDtoAsync(t, ct));
        }

        public async Task<ResponseDto<GenerateTemplatesResultDto>> GenerateTemplatesAsync(GenerateTemplatesDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.SystemJobs)) return Bad<GenerateTemplatesResultDto>(NotAllowed);
            FnPeriod p = dto == null ? null : await _uow.Set<FnPeriod>().Include(x => x.FiscalYear).FirstOrDefaultAsync(x => x.Id == dto.PeriodId, ct);
            if (p == null) return ResponseDtoHelper.NotFound<GenerateTemplatesResultDto>("دوره پیدا نشد.");
            if (p.Status != FnPeriodStatus.Open || p.FiscalYear.Status != FnYearStatus.Open) return Bad<GenerateTemplatesResultDto>("دوره باز نیست.");
            var result = new GenerateTemplatesResultDto();
            List<FnVoucherTemplate> templates = await _uow.Set<FnVoucherTemplate>().Include(t => t.Lines).Where(t => t.IsActive).OrderBy(t => t.Id).ToListAsync(ct);
            List<int> done = await _uow.Set<FnTemplateInstance>().Where(i => i.PeriodId == p.Id).Select(i => i.TemplateId).ToListAsync(ct);
            int days = (int)(p.EndDate.Date - p.StartDate.Date).TotalDays + 1;
            var created = new List<(FnVoucherTemplate Template, FnVoucher Voucher)>();
            foreach (FnVoucherTemplate t in templates)
            {
                if (done.Contains(t.Id)) { result.Skipped++; continue; }
                var v = new FnVoucher
                {
                    FiscalYearId = p.FiscalYearId, Date = p.StartDate.Date.AddDays(Math.Min(t.DayOfMonth, days) - 1), Type = FnVoucherType.Normal, Status = FnVoucherStatus.Draft,
                    Description = Clean(t.Description) ?? t.Title, CreatedByUserId = 0, CreatedAt = DateTimeOffset.Now,
                    TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, p.FiscalYear.Year, ct),
                    Lines = t.Lines.OrderBy(l => l.RowNumber).Select(l => new FnVoucherLine
                    {
                        RowNumber = l.RowNumber, AccountId = l.AccountId, Detail1Id = l.Detail1Id, Detail2Id = l.Detail2Id, Debit = l.Debit, Credit = l.Credit, Description = l.Description,
                    }).ToList(),
                };
                v.TotalAmount = v.Lines.Sum(l => l.Debit);
                _uow.Set<FnVoucher>().Add(v);
                created.Add((t, v));
            }
            if (created.Count > 0) await _uow.SaveChangesAsync(ct);
            foreach (var c in created)
            {
                _uow.Set<FnTemplateInstance>().Add(new FnTemplateInstance { TemplateId = c.Template.Id, PeriodId = p.Id, VoucherId = c.Voucher.Id });
                result.VoucherIds.Add(c.Voucher.Id);
                result.Drafted++;
            }
            if (created.Count > 0) await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }

        // ------------------------------------------------------------------------------------------ mass entry

        public async Task<ResponseDto<MassVoucherResultDto>> MassImportAsync(MassVoucherImportDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<MassVoucherResultDto>(NotAllowed);
            List<MassVoucherRowDto> rows = (dto?.Rows ?? new List<MassVoucherRowDto>()).Where(r => r != null).ToList();
            if (rows.Count == 0) return Bad<MassVoucherResultDto>("فایل هیچ ردیفی ندارد.");
            if (rows.Count > 5000) return Bad<MassVoucherResultDto>("حداکثر ۵٬۰۰۰ ردیف در هر بار.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, dto.LegalEntityId, Me, ct);
            var result = new MassVoucherResultDto { DryRun = dto.DryRun };
            var prepared = new List<(MassVoucherRowDto First, FnFiscalYear Year, List<FnVoucherLine> Lines)>();
            foreach (var group in rows.GroupBy(r => (Clean(r.Group) ?? "") + "|" + (r.Date.Date == default ? "" : r.Date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))))
            {
                MassVoucherRowDto first = group.First();
                string label = Clean(first.Group) ?? first.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (first.Date == default) { result.Errors.Add($"گروه {label}: تاریخ خالی است."); continue; }
                var check = await CheckDateAsync(first.Date.Date, entityId, ct);
                if (check.Error != null) { result.Errors.Add($"گروه {label}: {check.Error}"); continue; }
                var lines = group.Select(r => new SaveVoucherLineDto
                {
                    AccountCode = r.AccountCode, Detail1Code = r.Detail1Code, Detail2Code = r.Detail2Code, Debit = r.Debit, Credit = r.Credit, Description = r.LineDescription,
                }).ToList();
                var built = await BuildLinesAsync(lines, true, ct);
                if (built.Error != null) { result.Errors.Add($"گروه {label}: {built.Error}"); continue; }
                prepared.Add((first, check.Year, built.Lines));
            }
            result.Vouchers = prepared.Count;
            result.Lines = prepared.Sum(p => p.Lines.Count);
            if (result.Errors.Count > 0 || dto.DryRun) return ResponseDtoHelper.Ok(result);
            var vouchers = new List<FnVoucher>();
            foreach (var p in prepared)
            {
                var v = new FnVoucher
                {
                    FiscalYearId = p.Year.Id, Date = p.First.Date.Date, Type = FnVoucherType.Normal, Status = FnVoucherStatus.Draft, Description = Clean(p.First.Description),
                    CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, p.Year.Year, ct),
                    Lines = p.Lines, TotalAmount = p.Lines.Sum(l => l.Debit),
                };
                _uow.Set<FnVoucher>().Add(v);
                vouchers.Add(v);
            }
            await _uow.SaveChangesAsync(ct);
            result.VoucherIds = vouchers.Select(v => v.Id).ToList();
            return ResponseDtoHelper.Ok(result);
        }
    }
}
