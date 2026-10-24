using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // Finance release F3b: planned-versus-actual cost per work order, supplier credit / debit notes, supplier statement reconciliation
    // and customer confirmation letters.
    public partial class GlService
    {
        // ================================================================================== planned versus actual

        public async Task<ResponseDto<FnPlannedActualDto>> SaveEstimateAsync(FnSaveEstimateDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnPlannedActualDto>(NotAllowed);
            if (dto == null) return Bad<FnPlannedActualDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (dto.Material < 0 || dto.Labour < 0 || dto.Overhead < 0 || dto.Subcontract < 0) return Bad<FnPlannedActualDto>("مبلغ برآورد نمی‌تواند منفی باشد.");
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId, ct);
            if (wo == null) return ResponseDtoHelper.NotFound<FnPlannedActualDto>("دستور کار پیدا نشد.");
            int quantity = dto.Quantity > 0 ? dto.Quantity : wo.Count;
            // the earlier estimate stays as history; only one is active
            List<FnWorkOrderEstimate> old = await _uow.Set<FnWorkOrderEstimate>().Where(e => e.WorkOrderId == wo.Id && e.IsActive).ToListAsync(ct);
            foreach (FnWorkOrderEstimate e in old) e.IsActive = false;
            if (old.Count > 0) await _uow.SaveChangesAsync(ct);
            _uow.Set<FnWorkOrderEstimate>().Add(new FnWorkOrderEstimate
            {
                WorkOrderId = wo.Id, Material = R(dto.Material), Labour = R(dto.Labour), Overhead = R(dto.Overhead), Subcontract = R(dto.Subcontract), Quantity = quantity, Note = Clean(dto.Note),
                IsActive = true, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            });
            await _uow.SaveChangesAsync(ct);
            return await PlannedVsActualAsync(wo.Id, ct);
        }

        public async Task<ResponseDto<FnPlannedActualDto>> PlannedVsActualAsync(int workOrderId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnPlannedActualDto>(NotAllowed);
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, ct);
            if (wo == null) return ResponseDtoHelper.NotFound<FnPlannedActualDto>("دستور کار پیدا نشد.");
            FnWorkOrderEstimate est = await _uow.Set<FnWorkOrderEstimate>().AsNoTracking().Where(e => e.WorkOrderId == workOrderId && e.IsActive).OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);
            FnWorkOrderCostDto cost = await BuildCostAsync(wo, ct);
            var dto = new FnPlannedActualDto { WorkOrderId = wo.Id, Code = wo.Code, HasEstimate = est != null, IsClosed = cost.IsClosed, PlannedQuantity = est?.Quantity ?? 0, ActualQuantity = cost.Quantity };
            FnPlannedActualRowDto Row(string element, decimal planned, decimal actual) => new FnPlannedActualRowDto
            {
                Element = element, Planned = planned, Actual = actual, Variance = actual - planned, VariancePercent = planned == 0 ? (decimal?)null : Math.Round((actual - planned) * 100m / planned, 2, MidpointRounding.AwayFromZero),
            };
            dto.Rows.Add(Row("Material", est?.Material ?? 0, cost.Material));
            dto.Rows.Add(Row("Labour", est?.Labour ?? 0, cost.Labour));
            dto.Rows.Add(Row("Overhead", est?.Overhead ?? 0, cost.Overhead));
            dto.Rows.Add(Row("Subcontract", est?.Subcontract ?? 0, cost.Subcontract));
            dto.PlannedTotal = dto.Rows.Sum(r => r.Planned); dto.ActualTotal = dto.Rows.Sum(r => r.Actual); dto.Variance = dto.ActualTotal - dto.PlannedTotal;
            dto.VariancePercent = dto.PlannedTotal == 0 ? (decimal?)null : Math.Round(dto.Variance * 100m / dto.PlannedTotal, 2, MidpointRounding.AwayFromZero);
            dto.PlannedUnitCost = dto.PlannedQuantity > 0 ? R(dto.PlannedTotal / dto.PlannedQuantity) : dto.PlannedTotal;
            dto.ActualUnitCost = cost.UnitCost;
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== supplier credit and debit notes

        private FnSupplierNoteDto NoteDto(FnSupplierNote n, string providerName) => new FnSupplierNoteDto
        {
            Id = n.Id, Number = n.Number, Kind = n.Kind, ProviderId = n.ProviderId, ProviderName = providerName, SupplierInvoiceId = n.SupplierInvoiceId, NoteNumber = n.NoteNumber, Date = n.Date,
            NetAmount = n.NetAmount, VatAmount = n.VatAmount, TotalAmount = n.TotalAmount, Reason = n.Reason, Status = n.Status, VoucherId = n.VoucherId,
        };

        public async Task<ResponseDto<List<FnSupplierNoteDto>>> GetSupplierNotesAsync(int? providerId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnSupplierNoteDto>>(NotAllowed);
            IQueryable<FnSupplierNote> q = _uow.Set<FnSupplierNote>().AsNoTracking();
            if (providerId.HasValue) q = q.Where(n => n.ProviderId == providerId.Value);
            List<FnSupplierNote> rows = await q.OrderByDescending(n => n.Id).Take(500).ToListAsync(ct);
            List<int> ids = rows.Select(r => r.ProviderId).Distinct().ToList();
            Dictionary<int, string> names = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            return ResponseDtoHelper.Ok(rows.Select(r => NoteDto(r, names.GetValueOrDefault(r.ProviderId))).ToList());
        }

        public async Task<ResponseDto<FnSupplierNoteDto>> SaveSupplierNoteAsync(FnSupplierNoteInputDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnSupplierNoteDto>(NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(FnSupplierNoteKind), dto.Kind) || dto.Date == default) return Bad<FnSupplierNoteDto>("نوع، تاریخ و تامین‌کننده را وارد کنید.");
            if (Clean(dto.Reason) == null) return Bad<FnSupplierNoteDto>("علت صدور یادداشت را بنویسید.");
            decimal net = R(dto.NetAmount);
            if (net <= 0) return Bad<FnSupplierNoteDto>("مبلغ باید بیشتر از صفر باشد.");
            var provider = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == dto.ProviderId).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync(ct);
            if (provider == null) return Bad<FnSupplierNoteDto>("تامین‌کننده پیدا نشد.");
            Entities.Model.NasbNiro.Procurement.SupplierInvoice inv = null;
            if (dto.SupplierInvoiceId.HasValue)
            {
                inv = await _uow.Set<Entities.Model.NasbNiro.Procurement.SupplierInvoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == dto.SupplierInvoiceId.Value && !i.Deleted, ct);
                if (inv == null || inv.ProviderId != dto.ProviderId) return Bad<FnSupplierNoteDto>("فاکتور پیدا نشد یا مربوط به همین تامین‌کننده نیست.");
                if (inv.Status != Entities.Model.NasbNiro.Procurement.SupplierInvoiceStatus.Approved) return Bad<FnSupplierNoteDto>("فقط برای فاکتور تاییدشده یادداشت صادر می‌شود.");
            }
            decimal vat;
            if (dto.VatAmount.HasValue)
            {
                vat = R(dto.VatAmount.Value);
                if (vat < 0) return Bad<FnSupplierNoteDto>("مالیات ارزش افزوده نمی‌تواند منفی باشد.");
            }
            else if (inv != null && !inv.IsOfficial) vat = 0;
            else
            {
                decimal? rate = await TaxRates.RateAtAsync(_uow, FnTaxKind.Vat, dto.Date.Date, ct);
                if (!rate.HasValue) return Bad<FnSupplierNoteDto>("نرخ ارزش افزوده این تاریخ در جدول نرخ‌ها نیست؛ مبلغ مالیات را وارد کنید.");
                vat = R(net * rate.Value / 100m);
            }
            decimal total = net + vat;
            FnSupplierNote n = null;
            if (dto.Id.HasValue)
            {
                n = await _uow.Set<FnSupplierNote>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (n == null) return ResponseDtoHelper.NotFound<FnSupplierNoteDto>("یادداشت پیدا نشد.");
                if (n.Status != FnSettlementStatus.Draft) return Bad<FnSupplierNoteDto>("فقط پیش‌نویس ویرایش می‌شود.");
            }
            if (inv != null && dto.Kind == FnSupplierNoteKind.Credit)
            {
                int self = n?.Id ?? 0;
                decimal credited = await _uow.Set<FnSupplierNote>().Where(x => x.SupplierInvoiceId == inv.Id && x.Kind == FnSupplierNoteKind.Credit && x.Status != FnSettlementStatus.Cancelled && x.Id != self).SumAsync(x => (decimal?)x.TotalAmount, ct) ?? 0;
                if (credited + total > inv.TotalAmount) return Bad<FnSupplierNoteDto>($"جمع یادداشت‌های بستانکار ({credited + total:0}) از مبلغ فاکتور ({inv.TotalAmount:0}) بیشتر می‌شود.");
            }
            if (n == null)
            {
                n = new FnSupplierNote { Number = await NextNumberAsync(dto.Kind == FnSupplierNoteKind.Credit ? "SCN" : "SDN", FinanceNumbers.SupplierNote, dto.Date, ct), Status = FnSettlementStatus.Draft, PreparedByUserId = Me, PreparedAt = DateTimeOffset.Now };
                _uow.Set<FnSupplierNote>().Add(n);
            }
            n.EditorUserIds = WithEditor(n.EditorUserIds, Me); n.PreparedByUserId = Me; n.PreparedAt = DateTimeOffset.Now;
            n.Kind = dto.Kind; n.ProviderId = dto.ProviderId; n.SupplierInvoiceId = dto.SupplierInvoiceId; n.NoteNumber = Clean(dto.NoteNumber); n.Date = dto.Date.Date;
            n.NetAmount = net; n.VatAmount = vat; n.TotalAmount = total; n.Reason = Clean(dto.Reason);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(NoteDto(n, provider.Name));
        }

        public async Task<ResponseDto<FnSupplierNoteDto>> SupplierNoteActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnSupplierNoteDto>(NotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<FnSupplierNoteDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnSupplierNote n = await _uow.Set<FnSupplierNote>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (n == null) return ResponseDtoHelper.NotFound<FnSupplierNoteDto>("یادداشت پیدا نشد.");
            if (n.Status != FnSettlementStatus.Draft) return Bad<FnSupplierNoteDto>("فقط پیش‌نویس تایید یا باطل می‌شود.");
            string name = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == n.ProviderId).Select(p => p.Name).FirstOrDefaultAsync(ct);
            string action = dto.Action.Trim().ToLowerInvariant();
            if (action == "cancel") { n.Status = FnSettlementStatus.Cancelled; await _uow.SaveChangesAsync(ct); return ResponseDtoHelper.Ok(NoteDto(n, name)); }
            if (action != "approve") return Bad<FnSupplierNoteDto>("عمل نامعتبر است.");
            if (Touched(n.PreparedByUserId, n.EditorUserIds, Me)) return Bad<FnSupplierNoteDto>(SameUser);
            FnAccount payable = await RoleAccountAsync(AccountRoles.AccountsPayable, "30101", ct), ppv = await RoleAccountAsync(AccountRoles.PurchasePriceVariance, "70108", ct), vatAccount = await RoleAccountAsync(AccountRoles.Vat, "10403", ct);
            if (payable == null || ppv == null || vatAccount == null) return Bad<FnSupplierNoteDto>("حساب پرداختنی، اختلاف قیمت خرید یا مالیات ارزش افزوده خرید پیدا نشد.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            FnDetailAccount supplier = await new DetailResolver(_uow).GetAsync(FnDetailType.Supplier, n.ProviderId, ct);
            bool credit = n.Kind == FnSupplierNoteKind.Credit;
            string text = $"{(credit ? "یادداشت بستانکار" : "یادداشت بدهکار")} تامین‌کننده {n.Number}" + (n.NoteNumber == null ? "" : " - " + n.NoteNumber);
            var lines = new List<AutoLine>
            {
                new AutoLine { Account = payable, Detail1 = supplier, Debit = credit ? n.TotalAmount : 0, Credit = credit ? 0 : n.TotalAmount, Description = text },
                new AutoLine { Account = ppv, Debit = credit ? 0 : n.NetAmount, Credit = credit ? n.NetAmount : 0, Description = text },
                new AutoLine { Account = vatAccount, Debit = credit ? 0 : n.VatAmount, Credit = credit ? n.VatAmount : 0, Description = text },
            };
            var v = await AddAutoVoucherAsync(entityId, n.Date, text, "SupplierNote", n.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) return Bad<FnSupplierNoteDto>(v.Error);
            n.Voucher = v.Voucher; n.Status = FnSettlementStatus.Posted; n.ApprovedByUserId = Me; n.ApprovedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(NoteDto(n, name));
        }

        // ================================================================================== supplier statement reconciliation

        private sealed class OurItem
        {
            public DateTime Date; public string Kind; public string Number; public decimal Credit, Debit; public HashSet<string> Aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase); public bool Used;
        }

        private sealed class StatementCore
        {
            public string ProviderName;
            public decimal OurBalance;
            public List<OurItem> Ours = new List<OurItem>();
            public List<FnStatementLineDto> Lines = new List<FnStatementLineDto>();
            public string Error;
        }

        private async Task<StatementCore> StatementCoreAsync(int providerId, DateTime asOf, List<FnStatementLineInputDto> input, CancellationToken ct)
        {
            var core = new StatementCore();
            var provider = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == providerId).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync(ct);
            if (provider == null) { core.Error = "تامین‌کننده پیدا نشد."; return core; }
            core.ProviderName = provider.Name;
            FnAccount payable = await RoleAccountAsync(AccountRoles.AccountsPayable, "30101", ct);
            if (payable == null) { core.Error = "حساب پرداختنی تجاری پیدا نشد."; return core; }
            FnFiscalYear year = await LedgerYearAsync(asOf, ct);
            if (year == null) { core.Error = "سال مالی این تاریخ تعریف نشده است."; return core; }
            var rows = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.AccountId == payable.Id && l.Voucher.FiscalYearId == year.Id && l.Voucher.Status != FnVoucherStatus.Draft && l.Voucher.Date <= asOf && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose
                    && ((l.Detail1 != null && l.Detail1.Type == FnDetailType.Supplier && l.Detail1.LinkedEntityId == providerId) || (l.Detail2 != null && l.Detail2.Type == FnDetailType.Supplier && l.Detail2.LinkedEntityId == providerId)))
                .OrderBy(l => l.Voucher.Date).ThenBy(l => l.VoucherId).ThenBy(l => l.RowNumber)
                .Select(l => new { l.Voucher.Date, l.Voucher.Type, l.Voucher.SourceType, l.Voucher.SourceNumber, l.Voucher.Number, l.Voucher.TemporaryNumber, l.Debit, l.Credit }).ToListAsync(ct);
            Dictionary<string, string> invoiceNumbers = await _uow.Set<Entities.Model.NasbNiro.Procurement.SupplierInvoice>().AsNoTracking().Where(i => i.ProviderId == providerId && !i.Deleted && i.InvoiceNumber != null)
                .ToDictionaryAsync(i => i.Number, i => i.InvoiceNumber, ct);
            foreach (var r in rows)
            {
                string number = r.SourceNumber ?? (r.Number.HasValue ? r.Number.Value.ToString(CultureInfo.InvariantCulture) : "T" + r.TemporaryNumber.ToString(CultureInfo.InvariantCulture));
                var item = new OurItem { Date = r.Date, Kind = r.Type == FnVoucherType.Opening ? "Opening" : r.SourceType ?? "Voucher", Number = number, Credit = r.Credit, Debit = r.Debit };
                item.Aliases.Add(number);
                if (r.SourceNumber != null && invoiceNumbers.TryGetValue(r.SourceNumber, out string theirNumber)) item.Aliases.Add(theirNumber);
                core.Ours.Add(item);
            }
            core.OurBalance = core.Ours.Sum(i => i.Credit - i.Debit);
            int n = 0;
            foreach (FnStatementLineInputDto l in input)
            {
                n++;
                var line = new FnStatementLineDto { RowNumber = n, Date = l.Date.Date, DocumentNumber = Clean(l.DocumentNumber), Description = Clean(l.Description), Credit = R(l.Credit), Debit = R(l.Debit) };
                OurItem hit = null;
                bool Same(OurItem i) => !i.Used && i.Credit == line.Credit && i.Debit == line.Debit;
                if (line.DocumentNumber != null) hit = core.Ours.FirstOrDefault(i => Same(i) && i.Aliases.Contains(line.DocumentNumber));
                if (hit == null) hit = core.Ours.Where(Same).OrderBy(i => Math.Abs((i.Date - line.Date).TotalDays)).FirstOrDefault();
                if (hit != null) { hit.Used = true; line.MatchedKind = hit.Kind; line.MatchedNumber = hit.Number; }
                core.Lines.Add(line);
            }
            return core;
        }

        private FnSupplierStatementDto StatementDto(StatementCore core, int id, string number, int providerId, DateTime asOf, decimal their, string note)
        {
            var dto = new FnSupplierStatementDto
            {
                Id = id, Number = number, ProviderId = providerId, ProviderName = core.ProviderName, AsOf = asOf, TheirBalance = their, OurBalance = core.OurBalance, Difference = their - core.OurBalance,
                Lines = core.Lines, UnmatchedTheirs = core.Lines.Where(l => l.MatchedKind == null).ToList(),
                UnmatchedOurs = core.Ours.Where(i => !i.Used).Select(i => new FnOurItemDto { Date = i.Date, Kind = i.Kind, Number = i.Number, Credit = i.Credit, Debit = i.Debit }).ToList(),
            };
            dto.Explained = dto.UnmatchedTheirs.Sum(l => l.Credit - l.Debit) - dto.UnmatchedOurs.Sum(i => i.Credit - i.Debit);
            dto.Unexplained = dto.Difference - dto.Explained;
            dto.Reconciled = dto.Unexplained == 0;
            return dto;
        }

        public async Task<ResponseDto<FnSupplierStatementDto>> ReconcileSupplierStatementAsync(FnSupplierStatementInputDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnSupplierStatementDto>(NotAllowed);
            if (dto == null || dto.AsOf == default || dto.Lines == null) return Bad<FnSupplierStatementDto>("تامین‌کننده، تاریخ و ردیف‌های صورت‌حساب را وارد کنید.");
            if (dto.Lines.Count > 2000) return Bad<FnSupplierStatementDto>("حداکثر ۲۰۰۰ ردیف در هر صورت‌حساب پذیرفته می‌شود.");
            if (dto.Lines.Any(l => l == null || l.Credit < 0 || l.Debit < 0 || (l.Credit == 0 && l.Debit == 0) || (l.Credit != 0 && l.Debit != 0) || l.Date == default))
                return Bad<FnSupplierStatementDto>("هر ردیف باید تاریخ داشته باشد و فقط یکی از ستون‌های بستانکار یا بدهکار را با مبلغ مثبت پر کند.");
            DateTime asOf = dto.AsOf.Date;
            StatementCore core = await StatementCoreAsync(dto.ProviderId, asOf, dto.Lines, ct);
            if (core.Error != null) return Bad<FnSupplierStatementDto>(core.Error);
            decimal their = R(dto.TheirBalance);
            var st = new FnSupplierStatement
            {
                Number = await NextNumberAsync("SST", FinanceNumbers.SupplierStatement, asOf, ct), ProviderId = dto.ProviderId, AsOf = asOf, TheirBalance = their, OurBalance = core.OurBalance,
                Difference = their - core.OurBalance, Note = Clean(dto.Note), CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Lines = new List<FnSupplierStatementLine>(),
            };
            FnSupplierStatementDto result = StatementDto(core, 0, st.Number, dto.ProviderId, asOf, their, st.Note);
            st.Reconciled = result.Reconciled;
            foreach (FnStatementLineDto l in core.Lines)
                st.Lines.Add(new FnSupplierStatementLine { RowNumber = l.RowNumber, Date = l.Date, DocumentNumber = l.DocumentNumber, Description = l.Description, Credit = l.Credit, Debit = l.Debit, MatchedKind = l.MatchedKind, MatchedNumber = l.MatchedNumber });
            _uow.Set<FnSupplierStatement>().Add(st);
            await _uow.SaveChangesAsync(ct);
            result.Id = st.Id;
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<FnSupplierStatementDto>> GetSupplierStatementAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnSupplierStatementDto>(NotAllowed);
            FnSupplierStatement st = await _uow.Set<FnSupplierStatement>().AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (st == null) return ResponseDtoHelper.NotFound<FnSupplierStatementDto>("صورت‌حساب پیدا نشد.");
            var input = st.Lines.OrderBy(l => l.RowNumber).Select(l => new FnStatementLineInputDto { Date = l.Date, DocumentNumber = l.DocumentNumber, Description = l.Description, Credit = l.Credit, Debit = l.Debit }).ToList();
            StatementCore core = await StatementCoreAsync(st.ProviderId, st.AsOf, input, ct);
            if (core.Error != null) return Bad<FnSupplierStatementDto>(core.Error);
            return ResponseDtoHelper.Ok(StatementDto(core, st.Id, st.Number, st.ProviderId, st.AsOf, st.TheirBalance, st.Note));
        }

        // ================================================================================== customer confirmation letters

        private async Task<FnConfirmationDto> ConfirmationDtoAsync(FnConfirmationLetter c, CancellationToken ct)
        {
            FnCustomer customer = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CustomerId, ct);
            string company = await _uow.Set<FnLegalEntity>().AsNoTracking().Where(e => e.Id == FinanceSeed.MainEntityId).Select(e => e.Name).FirstOrDefaultAsync(ct);
            var statement = await _receivables.GetCustomerStatementAsync(c.CustomerId, ct);
            List<CustomerStatementRowDto> rows = statement.Success && statement.Data != null ? statement.Data.Rows.Where(r => r.Date.Date <= c.AsOf.Date).ToList() : new List<CustomerStatementRowDto>();
            return new FnConfirmationDto
            {
                Id = c.Id, Number = c.Number, CompanyName = company, CustomerId = c.CustomerId, CustomerName = customer?.Name, EconomicCode = customer?.EconomicCode, AsOf = c.AsOf, LedgerBalance = c.LedgerBalance,
                StatementBalance = c.StatementBalance, Agrees = c.LedgerBalance == c.StatementBalance, OpenDocuments = c.OpenDocuments, Status = c.Status, ConfirmedAmount = c.ConfirmedAmount, ResponseDate = c.ResponseDate,
                ResponseNote = c.ResponseNote, Rows = rows,
            };
        }

        public async Task<ResponseDto<List<FnConfirmationDto>>> GetConfirmationsAsync(int? customerId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnConfirmationDto>>(NotAllowed);
            IQueryable<FnConfirmationLetter> q = _uow.Set<FnConfirmationLetter>().AsNoTracking();
            if (customerId.HasValue) q = q.Where(c => c.CustomerId == customerId.Value);
            var list = new List<FnConfirmationDto>();
            foreach (FnConfirmationLetter c in await q.OrderByDescending(c => c.Id).Take(200).ToListAsync(ct)) list.Add(await ConfirmationDtoAsync(c, ct));
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<FnConfirmationDto>> IssueConfirmationAsync(FnConfirmationInputDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnConfirmationDto>(NotAllowed);
            if (dto == null || dto.AsOf == default) return Bad<FnConfirmationDto>("مشتری و تاریخ را وارد کنید.");
            DateTime asOf = dto.AsOf.Date;
            FnCustomer customer = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.CustomerId, ct);
            if (customer == null) return ResponseDtoHelper.NotFound<FnConfirmationDto>("مشتری پیدا نشد.");
            FnAccount receivable = await RoleAccountAsync(AccountRoles.AccountsReceivable, "10301", ct);
            if (receivable == null) return Bad<FnConfirmationDto>("حساب دریافتنی تجاری پیدا نشد.");
            FnFiscalYear year = await LedgerYearAsync(asOf, ct);
            if (year == null) return Bad<FnConfirmationDto>("سال مالی این تاریخ تعریف نشده است.");
            var sums = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.AccountId == receivable.Id && l.Voucher.FiscalYearId == year.Id && l.Voucher.Status != FnVoucherStatus.Draft && l.Voucher.Date <= asOf && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose
                    && ((l.Detail1 != null && l.Detail1.Type == FnDetailType.Customer && l.Detail1.LinkedEntityId == dto.CustomerId) || (l.Detail2 != null && l.Detail2.Type == FnDetailType.Customer && l.Detail2.LinkedEntityId == dto.CustomerId)))
                .GroupBy(l => 1).Select(g => new { D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) }).FirstOrDefaultAsync(ct);
            decimal ledger = sums == null ? 0 : sums.D - sums.C;
            var statement = await _receivables.GetCustomerStatementAsync(dto.CustomerId, ct);
            if (!statement.Success) return Bad<FnConfirmationDto>(statement.Message);
            decimal stmt = statement.Data.Rows.Where(r => r.Date.Date <= asOf).Select(r => r.Balance).LastOrDefault();
            // invoices still open on that date
            var invoices = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.CustomerId == dto.CustomerId && i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote && i.Date <= asOf)
                .Select(i => new { i.Id, i.ReceivableAmount }).ToListAsync(ct);
            List<int> ids = invoices.Select(i => i.Id).ToList();
            var paid = await _uow.Set<FnReceiptAllocation>().AsNoTracking().Where(a => ids.Contains(a.InvoiceId) && a.Receipt.Status == FnDocStatus.Posted && a.Receipt.Date <= asOf)
                .GroupBy(a => a.InvoiceId).Select(g => new { g.Key, Amount = g.Sum(a => a.Amount) }).ToListAsync(ct);
            var credited = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.CreditedInvoiceId != null && ids.Contains(i.CreditedInvoiceId.Value) && i.Type == FnInvoiceType.CreditNote && i.Status == FnDocStatus.Posted && i.Date <= asOf)
                .GroupBy(i => i.CreditedInvoiceId.Value).Select(g => new { g.Key, Amount = g.Sum(i => i.ReceivableAmount) }).ToListAsync(ct);
            int open = invoices.Count(i => i.ReceivableAmount - (paid.FirstOrDefault(p => p.Key == i.Id)?.Amount ?? 0) - (credited.FirstOrDefault(p => p.Key == i.Id)?.Amount ?? 0) > 0);
            var c = new FnConfirmationLetter
            {
                Number = await NextNumberAsync("CNF", FinanceNumbers.ConfirmationLetter, asOf, ct), CustomerId = dto.CustomerId, AsOf = asOf, LedgerBalance = ledger, StatementBalance = stmt, OpenDocuments = open,
                Status = FnConfirmationStatus.Issued, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnConfirmationLetter>().Add(c);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ConfirmationDtoAsync(c, ct));
        }

        public async Task<ResponseDto<FnConfirmationDto>> RespondConfirmationAsync(FnConfirmationRespondDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnConfirmationDto>(NotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<FnConfirmationDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnConfirmationLetter c = await _uow.Set<FnConfirmationLetter>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (c == null) return ResponseDtoHelper.NotFound<FnConfirmationDto>("نامه تایید مانده پیدا نشد.");
            if (c.Status != FnConfirmationStatus.Issued) return Bad<FnConfirmationDto>("به این نامه قبلاً پاسخ ثبت شده است.");
            string action = dto.Action.Trim().ToLowerInvariant();
            if (action == "confirm")
            {
                if (dto.Amount.HasValue && R(dto.Amount.Value) != c.LedgerBalance) return Bad<FnConfirmationDto>("مبلغ تاییدشده با مانده دفتر یکسان نیست؛ در این حالت مغایرت را ثبت کنید.");
                c.Status = FnConfirmationStatus.Confirmed; c.ConfirmedAmount = c.LedgerBalance;
            }
            else if (action == "dispute")
            {
                if (!dto.Amount.HasValue || Clean(dto.Note) == null) return Bad<FnConfirmationDto>("برای ثبت مغایرت، مبلغ اعلامی مشتری و توضیح را وارد کنید.");
                c.Status = FnConfirmationStatus.Disputed; c.ConfirmedAmount = R(dto.Amount.Value);
            }
            else return Bad<FnConfirmationDto>("عمل نامعتبر است.");
            c.ResponseDate = (dto.Date ?? DateTime.Today).Date; c.ResponseNote = Clean(dto.Note);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ConfirmationDtoAsync(c, ct));
        }
    }
}
