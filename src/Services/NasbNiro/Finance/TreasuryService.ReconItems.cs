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
    // R1t D7: a bank reconciliation with a difference can be approved only when the difference is explained by a list of reconciling items (kind, signed amount, reason) that adds up to
    // exactly the difference. The list is saved by treasury or finance (action "items"); it is approved by the finance manager, who is neither the preparer nor the person who saved the list.
    public partial class TreasuryService
    {
        private const int MaxReconItems = 50;

        /// <summary>The kinds of reconciling item. Every line of the statement must still be matched: the items explain what is left between the books and the statement, they never replace a match.</summary>
        internal static readonly IReadOnlyDictionary<int, string> ReconKinds = new Dictionary<int, string>
        {
            { 1, "کارمزد یا سود بانکی که هنوز ثبت نشده" },
            { 2, "واریز یا پرداخت در راه" },
            { 3, "خطای بانک" },
            { 4, "خطا در دفاتر شرکت" },
            { 5, "مانده قبل از شروع کار سیستم" },
            { 9, "سایر (علت را بنویسید)" },
        };

        private static string ReconKindTitle(int kind) => ReconKinds.TryGetValue(kind, out string title) ? title : "نامشخص";

        private static ReconciliationItemDto ToItemDto(FnBankReconciliationItem i) => new ReconciliationItemDto { Kind = i.Kind, KindTitle = ReconKindTitle(i.Kind), Amount = i.Amount, Note = i.Note, CorrectingVoucherId = i.CorrectingVoucherId };

        /// <summary>Replaces the saved reconciling items of a draft reconciliation. Returns the refusal text, or null (nothing is saved here).</summary>
        private async Task<string> SaveReconItemsAsync(FnBankReconciliation rec, List<ReconciliationItemDto> input, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.TreasuryAndAccountants)) return NotAllowed;
            if (rec.Status != 0) return "مغایرت‌گیری تاییدشده تغییر نمی‌کند.";
            List<ReconciliationItemDto> list = (input ?? new List<ReconciliationItemDto>()).Where(i => i != null).ToList();
            if (list.Count > MaxReconItems) return $"حداکثر {MaxReconItems} قلم مغایرت ثبت می‌شود.";
            var rows = new List<FnBankReconciliationItem>();
            int row = 1;
            foreach (ReconciliationItemDto i in list)
            {
                decimal amount = R(i.Amount);
                if (amount == 0) return $"قلم {row}: مبلغ نباید صفر باشد.";
                if (!ReconKinds.ContainsKey(i.Kind)) return $"قلم {row}: نوع قلم نامعتبر است.";
                string note = Clean(i.Note);
                if (note == null) return $"قلم {row}: علت مغایرت را بنویسید.";
                if (note.Length > 500) return $"قلم {row}: توضیح بیش از ۵۰۰ نویسه است.";
                int? voucherId = i.CorrectingVoucherId;
                if (voucherId.HasValue)
                {
                    if (i.Kind != 1 && i.Kind != 4) return $"قلم {row}: سند اصلاحی فقط برای کارمزد یا سود ثبت‌نشده و خطای دفاتر شرکت است.";
                    if (!await _uow.Set<FnVoucher>().AnyAsync(v => v.Id == voucherId.Value, ct)) return $"قلم {row}: سند اصلاحی پیدا نشد.";
                }
                rows.Add(new FnBankReconciliationItem { ReconciliationId = rec.Id, Kind = i.Kind, Amount = amount, Note = note, CorrectingVoucherId = voucherId });
                row++;
            }
            List<FnBankReconciliationItem> old = await _uow.Set<FnBankReconciliationItem>().Where(x => x.ReconciliationId == rec.Id).ToListAsync(ct);
            _uow.Set<FnBankReconciliationItem>().RemoveRange(old);
            foreach (FnBankReconciliationItem r in rows) _uow.Set<FnBankReconciliationItem>().Add(r);
            rec.ExplainedAmount = rows.Sum(x => x.Amount);
            rec.ItemsByUserId = rows.Count == 0 ? (int?)null : Me;
            return null;
        }

        /// <summary>The approval rule for the difference: it must equal the sum of the saved items (zero when there is no difference and no item), and the person who saved the items cannot approve.</summary>
        private async Task<string> ReconDifferenceRefusalAsync(FnBankReconciliation rec, decimal difference, CancellationToken ct)
        {
            decimal explained = await _uow.Set<FnBankReconciliationItem>().Where(x => x.ReconciliationId == rec.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            // M8 (review R1t): an unbooked bank fee or interest (1) and an error in our books (4) are not left as a permanent plug: the voucher that books the correction must be linked and final
            var needVoucher = await _uow.Set<FnBankReconciliationItem>().AsNoTracking().Where(x => x.ReconciliationId == rec.Id && (x.Kind == 1 || x.Kind == 4)).Select(x => x.CorrectingVoucherId).ToListAsync(ct);
            if (needVoucher.Any(v => !v.HasValue)) return "برای قلم‌های «کارمزد یا سود ثبت‌نشده» و «خطا در دفاتر شرکت» سند اصلاحی را وصل کنید؛ بدون آن مغایرت‌گیری تایید نمی‌شود.";
            List<int> linked = needVoucher.Where(v => v.HasValue).Select(v => v.Value).ToList();
            if (linked.Count > 0 && await _uow.Set<FnVoucher>().CountAsync(v => linked.Contains(v.Id) && v.Status == FnVoucherStatus.Final, ct) < linked.Distinct().Count()) return "سند اصلاحی وصل‌شده هنوز قطعی نشده است؛ ابتدا سند را قطعی کنید.";
            if (difference == explained)
            {
                if (difference != 0 && rec.ItemsByUserId.HasValue && rec.ItemsByUserId.Value == Me) return "کسی که اقلام مغایرت را وارد کرده نمی‌تواند همان مغایرت‌گیری را تایید کند.";
                return null;
            }
            if (explained == 0) return $"مغایرت صفر نیست ({difference:0} ریال)؛ ردیف‌های تطبیق‌نخورده را رفع کنید یا اقلام مغایرت را با علت و مبلغ ثبت کنید.";
            return $"جمع اقلام مغایرت ({explained:0} ریال) با مغایرت محاسبه‌شده ({difference:0} ریال) برابر نیست؛ اقلام را اصلاح کنید.";
        }
    }
}
