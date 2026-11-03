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

        private static ReconciliationItemDto ToItemDto(FnBankReconciliationItem i) => new ReconciliationItemDto { Kind = i.Kind, KindTitle = ReconKindTitle(i.Kind), Amount = i.Amount, Note = i.Note };

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
                rows.Add(new FnBankReconciliationItem { ReconciliationId = rec.Id, Kind = i.Kind, Amount = amount, Note = note });
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
