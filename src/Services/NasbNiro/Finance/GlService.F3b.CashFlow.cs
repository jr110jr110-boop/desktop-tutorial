using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    /// <summary>Finance release F3b: everything that completes the F3 modules (see docs of the release).</summary>
    public interface IFinanceCompletenessService
    {
        Task<ResponseDto<FnDirectCashFlowDto>> DirectCashFlowAsync(FnStatementQuery query, CancellationToken cancellationToken);

        Task<ResponseDto<List<FnBankGuaranteeDto>>> GetBankGuaranteesAsync(int? direction, CancellationToken cancellationToken);
        Task<ResponseDto<FnBankGuaranteeDto>> SaveBankGuaranteeAsync(FnSaveBankGuaranteeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnBankGuaranteeDto>> BankGuaranteeActionAsync(FnGuaranteeActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnGuaranteeLimitDto>> SetGuaranteeLimitAsync(FnGuaranteeLimitDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnGuaranteeExposureDto>> GuaranteeExposureAsync(DateTime? asOf, CancellationToken cancellationToken);

        Task<ResponseDto<List<FnClearanceDto>>> GetClearancesAsync(int? providerId, DateTime? asOf, CancellationToken cancellationToken);
        Task<ResponseDto<FnClearanceDto>> SaveClearanceAsync(FnSaveClearanceDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnRetentionStatusDto>> RetentionStatusAsync(int providerId, int? contractId, DateTime? asOf, CancellationToken cancellationToken);
        Task<ResponseDto<FnRetentionReleaseDto>> ReleaseRetentionAsync(FnRetentionReleaseRequestDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<FnTerminationDto>>> GetTerminationsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnTerminationDto>> SaveTerminationAsync(FnTerminationInputDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnTerminationDto>> TerminationActionAsync(DocActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnBonusRunDto>>> GetBonusRunsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnBonusRunDto>> SaveBonusRunAsync(FnBonusInputDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnBonusRunDto>> BonusRunActionAsync(DocActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnTaxReconciliationDto>> PayrollTaxReconciliationAsync(int year, CancellationToken cancellationToken);

        Task<ResponseDto<FnPlannedActualDto>> SaveEstimateAsync(FnSaveEstimateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnPlannedActualDto>> PlannedVsActualAsync(int workOrderId, CancellationToken cancellationToken);

        Task<ResponseDto<List<FnSupplierNoteDto>>> GetSupplierNotesAsync(int? providerId, CancellationToken cancellationToken);
        Task<ResponseDto<FnSupplierNoteDto>> SaveSupplierNoteAsync(FnSupplierNoteInputDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnSupplierNoteDto>> SupplierNoteActionAsync(DocActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnSupplierStatementDto>> ReconcileSupplierStatementAsync(FnSupplierStatementInputDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnSupplierStatementDto>> GetSupplierStatementAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<FnConfirmationDto>> IssueConfirmationAsync(FnConfirmationInputDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnConfirmationDto>> RespondConfirmationAsync(FnConfirmationRespondDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnConfirmationDto>>> GetConfirmationsAsync(int? customerId, CancellationToken cancellationToken);

        Task<ResponseDto<FnComparativeDto>> ComparativeAsync(string kind, int? fiscalYearId, bool includeDraft, CancellationToken cancellationToken);
        Task<ResponseDto<FnNotesDto>> NotesAsync(int? fiscalYearId, CancellationToken cancellationToken);
    }

    // Direct-method cash flow (F3b): every cash and bank movement of the ledger is classified by the account on the other side of its voucher.
    public partial class GlService : IFinanceCompletenessService
    {
        private sealed class CashClass { public string Key; public string Title; public string Section; }

        /// <summary>Purpose of a cash movement from the account code on the other side of the voucher (documented in the release notes).</summary>
        private static CashClass ClassifyCash(string code)
        {
            CashClass C(string key, string title, string section) => new CashClass { Key = key, Title = title, Section = section };
            if (code == "30203" || code == "30204" || code == "30205" || code == "10403" || code == "10306" || code == "10602" || code.StartsWith("30401", StringComparison.Ordinal) || code.StartsWith("804", StringComparison.Ordinal) || code.StartsWith("80401", StringComparison.Ordinal))
                return C("Taxes", "مالیات و عوارض (حقوق، تکلیفی، ارزش افزوده، درآمد)", "Operating");
            if (code == "30202" || code == "30207" || code == "10307" || code == "10603") return C("Insurance", "پرداخت حق بیمه", "Operating");
            if (code == "30201" || code == "10401") return C("Employees", "پرداخت به کارکنان (حقوق و مساعده)", "Operating");
            if (code.StartsWith("103", StringComparison.Ordinal) || code.StartsWith("303", StringComparison.Ordinal)) return C("Customers", "دریافت از مشتریان (شامل پیش‌دریافت)", "Operating");
            if (code.StartsWith("301", StringComparison.Ordinal) || code.StartsWith("105", StringComparison.Ordinal) || code.StartsWith("106", StringComparison.Ordinal) || code.StartsWith("107", StringComparison.Ordinal))
                return C("Suppliers", "پرداخت به تامین‌کنندگان، خرید کالا و پیش‌پرداخت‌ها", "Operating");
            if (code.StartsWith("302", StringComparison.Ordinal)) return C("OtherPayables", "سایر پرداختنی‌ها (سپرده‌ها و متفرقه)", "Operating");
            if (code.StartsWith("104", StringComparison.Ordinal)) return C("OtherReceivables", "سایر دریافتنی‌ها", "Operating");
            if (code.StartsWith("304", StringComparison.Ordinal) || code.StartsWith("402", StringComparison.Ordinal)) return C("Provisions", "پرداخت از محل ذخایر و مزایای پایان خدمت", "Operating");
            if (code.StartsWith("201", StringComparison.Ordinal) || code.StartsWith("202", StringComparison.Ordinal) || code.StartsWith("203", StringComparison.Ordinal) || code.StartsWith("204", StringComparison.Ordinal) || code.StartsWith("205", StringComparison.Ordinal) || code.StartsWith("102", StringComparison.Ordinal))
                return C("FixedAssets", "خرید و فروش دارایی ثابت و سرمایه‌گذاری", "Investing");
            if (code.StartsWith("305", StringComparison.Ordinal) || code.StartsWith("401", StringComparison.Ordinal)) return C("Loans", "دریافت و بازپرداخت تسهیلات", "Financing");
            if (code.StartsWith("5", StringComparison.Ordinal)) return C("Equity", "سرمایه و سود سهام", "Financing");
            if (code.StartsWith("6", StringComparison.Ordinal)) return C("OtherReceipts", "سایر دریافت‌های عملیاتی (فروش مستقیم، سود سپرده)", "Operating");
            if (code.StartsWith("7", StringComparison.Ordinal) || code.StartsWith("8", StringComparison.Ordinal)) return C("OperatingCosts", "پرداخت هزینه‌های عملیاتی و مالی", "Operating");
            return C("Other", "سایر", "Operating");
        }

        public async Task<ResponseDto<FnDirectCashFlowDto>> DirectCashFlowAsync(FnStatementQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnDirectCashFlowDto>(NotAllowed);
            var d = await StatementDataAsync(query, ct);
            if (d.Error != null) return Bad<FnDirectCashFlowDto>(d.Error);
            bool includeDraft = query?.IncludeDraft ?? false;
            IQueryable<FnVoucherLine> lines = _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.Voucher.FiscalYearId == d.Year.Id && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose
                && l.Voucher.Type != FnVoucherType.Opening && l.Voucher.Date >= d.From && l.Voucher.Date <= d.To);
            if (!includeDraft) lines = lines.Where(l => l.Voucher.Status == FnVoucherStatus.Final);
            IQueryable<int> cashVouchers = lines.Where(l => l.Account.Code.StartsWith("101")).Select(l => l.VoucherId).Distinct();
            var rows = await lines.Where(l => cashVouchers.Contains(l.VoucherId)).Select(l => new { l.VoucherId, l.Account.Code, l.Debit, l.Credit }).ToListAsync(ct);

            var dto = new FnDirectCashFlowDto { FiscalYearId = d.Year.Id, From = d.From, To = d.To };
            Func<string, bool> isCash = c => Starts(c, "101");
            dto.OpeningCash = Net(d.Rows, isCash, true);
            dto.ClosingCash = dto.OpeningCash + Net(d.Rows, isCash, false);
            var byKey = new Dictionary<string, FnDirectRowDto>();
            foreach (var voucher in rows.GroupBy(r => r.VoucherId))
            {
                // each non-cash line of a voucher that touches cash is the other side of the cash movement: its opposite amount is what moved in or out
                foreach (var counter in voucher.Where(r => !r.Code.StartsWith("101", StringComparison.Ordinal)))
                {
                    decimal cash = counter.Credit - counter.Debit;   // credit on the other side = cash came in
                    if (cash == 0) continue;
                    CashClass cls = ClassifyCash(counter.Code);
                    if (!byKey.TryGetValue(cls.Key, out FnDirectRowDto row))
                    {
                        row = new FnDirectRowDto { Key = cls.Key, Title = cls.Title, Section = cls.Section };
                        byKey[cls.Key] = row;
                    }
                    if (cash > 0) row.Receipts += cash; else row.Payments += -cash;
                    row.Net += cash;
                    if (!row.Accounts.Contains(counter.Code)) row.Accounts.Add(counter.Code);
                }
            }
            foreach (FnDirectRowDto r in byKey.Values) r.Accounts = r.Accounts.OrderBy(a => a, StringComparer.Ordinal).ToList();
            dto.Rows = byKey.Values.OrderBy(r => r.Section == "Operating" ? 0 : r.Section == "Investing" ? 1 : 2).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
            dto.Operating = dto.Rows.Where(r => r.Section == "Operating").Sum(r => r.Net);
            dto.Investing = dto.Rows.Where(r => r.Section == "Investing").Sum(r => r.Net);
            dto.Financing = dto.Rows.Where(r => r.Section == "Financing").Sum(r => r.Net);
            dto.NetChange = dto.Operating + dto.Investing + dto.Financing;

            // reconciliation with the indirect method of the same period
            var indirect = await CashFlowAsync(query, ct);
            if (indirect.Success && indirect.Data != null)
            {
                dto.IndirectNetChange = indirect.Data.NetChange;
                dto.Reconciliation.Add(new FnCashReconRowDto { Section = "Operating", Direct = dto.Operating, Indirect = indirect.Data.Operating, Difference = dto.Operating - indirect.Data.Operating });
                dto.Reconciliation.Add(new FnCashReconRowDto { Section = "Investing", Direct = dto.Investing, Indirect = indirect.Data.Investing, Difference = dto.Investing - indirect.Data.Investing });
                dto.Reconciliation.Add(new FnCashReconRowDto { Section = "Financing", Direct = dto.Financing, Indirect = indirect.Data.Financing, Difference = dto.Financing - indirect.Data.Financing });
                dto.Reconciliation.Add(new FnCashReconRowDto { Section = "Total", Direct = dto.NetChange, Indirect = indirect.Data.NetChange, Difference = dto.NetChange - indirect.Data.NetChange });
            }
            dto.Ties = dto.NetChange == dto.ClosingCash - dto.OpeningCash && dto.NetChange == dto.IndirectNetChange;
            dto.Note = "طبقه‌بندی بر اساس حساب طرف مقابل هر حرکت صندوق و بانک در سند است. تفاوت بخش‌ها با روش غیرمستقیم فقط از تفاوت طبقه‌بندی (مثلاً فروش دارایی یا مالیات) می‌آید؛ جمع هر دو روش باید با تغییر وجه نقد برابر باشد.";
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
