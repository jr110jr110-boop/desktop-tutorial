using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // R1t D7: treasury side of the cash legs of loans, letters of credit and bank guarantees. The documents are drafted by the facility modules (GlService.CashLegs.cs); here they are
    // protected from hand editing, approved by the finance manager, settled by a treasury user other than the person who recorded or approved them, and released again when cancelled.
    public partial class TreasuryService
    {
        private const string FacilityOnly = "سند پرداخت یا دریافت تسهیلات، اعتبار اسنادی و ضمانت‌نامه فقط از خود آن رکورد ساخته می‌شود و در اینجا ساخته یا ویرایش نمی‌شود؛ در صورت اشتباه، سند را باطل کنید و از رکورد اصلی دوباره ثبت کنید.";
        private const string FacilityRecorder = "ثبت‌کننده رکورد (تسهیلات، اعتبار اسنادی یا ضمانت‌نامه) و تاییدکننده سند خزانه نمی‌توانند وجه همان سند را نهایی کنند؛ کاربر دیگری از خزانه آن را ثبت نهایی کند.";

        private static CashLegRequest LegOf(FnPayment p) => new CashLegRequest
        {
            SourceType = p.SourceType, SourceId = p.SourceId ?? 0, Action = p.SourceAction, Data = p.SourceData, Inflow = false, BankAmount = p.NetAmount,
            BankAccountId = p.Lines.Where(l => l.BankAccountId.HasValue).Select(l => l.BankAccountId.Value).FirstOrDefault(), Date = p.Date, DocumentNumber = p.Number, RecordedByUserId = p.CreatedByUserId,
        };

        private static CashLegRequest LegOf(FnReceipt r) => new CashLegRequest
        {
            SourceType = r.SourceType, SourceId = r.SourceId ?? 0, Action = r.SourceAction, Data = r.SourceData, Inflow = true, BankAmount = r.TotalAmount,
            BankAccountId = r.Lines.Where(l => l.BankAccountId.HasValue).Select(l => l.BankAccountId.Value).FirstOrDefault(), Date = r.Date, DocumentNumber = r.Number, RecordedByUserId = r.CreatedByUserId,
        };

        /// <summary>
        /// Who may act on a cash leg. The finance manager approves (never the person who recorded the facility action); treasury posts or cancels; the person who recorded the leg
        /// and the person who approved it cannot post it. Returns the refusal text or null.
        /// </summary>
        private string FacilityActionRefusal(string action, int creatorUserId, string editorUserIds, int? approvedByUserId)
        {
            switch (action)
            {
                case "approve":
                    if (!_user.IsInRole(FinanceRoles.Managers)) return NotAllowed + " (مدیر مالی)";
                    return null;   // the generic code refuses the person who recorded it
                case "post":
                    if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return NotAllowed + " (خزانه‌داری)";
                    if (Touched(creatorUserId, editorUserIds, Me) || (approvedByUserId.HasValue && approvedByUserId.Value == Me)) return FacilityRecorder;
                    return null;
                case "cancel":
                    if (!_user.IsInRole(FinanceRoles.TreasuryUsers)) return NotAllowed + " (خزانه‌داری)";
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>Posts a payment cash leg: the facility module builds the ledger side and completes its record, treasury adds the bank line (so the bank book and the reconciliation see the leg).</summary>
        private async Task<string> PostFacilityPaymentAsync(FnPayment p, DocActionDto dto, CancellationToken ct)
        {
            if (p.Status != FnDocStatus.Approved) return "فقط سند تاییدشده توسط مدیر مالی ثبت نهایی می‌شود.";
            FnPaymentLine line = p.Lines.Count == 1 ? p.Lines.First() : null;
            if (line == null || line.Instrument != FnInstrument.BankTransfer || !line.BankAccountId.HasValue) return "سند پرداخت تسهیلات فقط یک ردیف حواله بانکی دارد.";
            if (Clean(line.Reference) == null)
            {
                if (Clean(dto.Reference) == null) return "شماره پیگیری حواله بانکی را وارد کنید.";
                line.Reference = Clean(dto.Reference);
            }
            FnBankAccount bank = await _uow.Set<FnBankAccount>().FirstOrDefaultAsync(b => b.Id == line.BankAccountId.Value, ct);
            if (bank == null || !bank.IsActive) return "حساب بانکی سند غیرفعال شده است.";
            if (IsForeignBank(bank)) return "حساب بانکی ارزی در این مسیر پشتیبانی نمی‌شود.";
            CashLegSettlement settlement = await _legs.SettleAsync(LegOf(p), ct);
            if (settlement.Error != null) return settlement.Error;
            var eventLines = new List<AccountingEventLine>(settlement.CounterLines) { BankLine(bank, 0, line.Amount, p.Number + " - " + line.Reference) };
            _costing.AddEvent(settlement.EventType, p.Date, "FnPayment", p.Id, p.Number, $"پرداخت {p.Number} - {settlement.Text}", eventLines);
            p.Status = FnDocStatus.Posted; p.PostedByUserId = Me; p.PostedAt = DateTimeOffset.Now;
            return null;
        }

        /// <summary>Posts a receipt cash leg (a loan paid out to us, a letter-of-credit margin coming back).</summary>
        private async Task<string> PostFacilityReceiptAsync(FnReceipt r, DocActionDto dto, CancellationToken ct)
        {
            if (r.Status != FnDocStatus.Approved) return "فقط سند تاییدشده ثبت نهایی می‌شود.";
            FnReceiptLine line = r.Lines.Count == 1 ? r.Lines.First() : null;
            if (line == null || line.Instrument != FnInstrument.BankTransfer || !line.BankAccountId.HasValue) return "سند دریافت تسهیلات فقط یک ردیف واریز بانکی دارد.";
            if (Clean(line.Reference) == null)
            {
                if (Clean(dto.Reference) == null) return "شماره پیگیری واریز بانکی را وارد کنید.";
                line.Reference = Clean(dto.Reference);
            }
            FnBankAccount bank = await _uow.Set<FnBankAccount>().FirstOrDefaultAsync(b => b.Id == line.BankAccountId.Value, ct);
            if (bank == null || !bank.IsActive) return "حساب بانکی سند غیرفعال شده است.";
            if (IsForeignBank(bank)) return "حساب بانکی ارزی در این مسیر پشتیبانی نمی‌شود.";
            CashLegSettlement settlement = await _legs.SettleAsync(LegOf(r), ct);
            if (settlement.Error != null) return settlement.Error;
            var eventLines = new List<AccountingEventLine>(settlement.CounterLines) { BankLine(bank, line.Amount, 0, r.Number + " - " + line.Reference) };
            _costing.AddEvent(settlement.EventType, r.Date, "FnReceipt", r.Id, r.Number, $"دریافت {r.Number} - {settlement.Text}", eventLines);
            r.Status = FnDocStatus.Posted; r.PostedByUserId = Me; r.PostedAt = DateTimeOffset.Now;
            return null;
        }
    }
}
