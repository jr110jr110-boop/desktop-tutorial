using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Entities.Model.NasbNiro.Finance;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t: the rules that keep an approval tied to what was approved (D9). A CEO approval belongs to the content the CEO saw:
    /// any later change of a header field or a line voids it, and the finalising gate accepts only an approval that is newer than the last edit.
    /// </summary>
    public partial class GlService
    {
        /// <summary>
        /// A text that changes when, and only when, something the CEO approved changes: the date, the description, the accrual flag
        /// and, row by row, the account, both details, the amounts, the currency data, the quantity, the row text and the three analysis links.
        /// </summary>
        internal static string VoucherSignature(DateTime date, string description, bool reverseNextPeriod, IEnumerable<FnVoucherLine> lines)
        {
            var sb = new StringBuilder();
            sb.Append(date.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append('|').Append(Clean(description)).Append('|').Append(reverseNextPeriod ? '1' : '0');
            foreach (FnVoucherLine l in (lines ?? Enumerable.Empty<FnVoucherLine>()).OrderBy(x => x.RowNumber))
            {
                sb.Append('\n').Append(l.AccountId).Append('|').Append(l.Detail1Id).Append('|').Append(l.Detail2Id)
                  .Append('|').Append(Num(l.Debit)).Append('|').Append(Num(l.Credit))
                  .Append('|').Append(l.CurrencyCode).Append('|').Append(l.CurrencyAmount.HasValue ? Num(l.CurrencyAmount.Value) : "").Append('|').Append(l.Rate.HasValue ? Num(l.Rate.Value) : "")
                  .Append('|').Append(l.Quantity.HasValue ? Num(l.Quantity.Value) : "").Append('|').Append(Clean(l.Description))
                  .Append('|').Append(l.WorkOrderId).Append('|').Append(l.ContractId).Append('|').Append(l.CostCenterId);
            }
            return sb.ToString();
        }

        private static string Num(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);

        /// <summary>
        /// Records that the voucher was edited. If the CEO had approved it, the approval is voided (the new content has not been seen) and the voiding is written
        /// to the audit trail with the approver and the time of the approval that no longer counts.
        /// </summary>
        private void MarkVoucherEdited(FnVoucher v)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            v.LastEditedAt = now;
            if (!v.CeoApprovedByUserId.HasValue) return;
            AuditNotes.Add(_uow, Me, nameof(FnVoucher), v.Id, "CeoApprovalVoid",
                $"تایید مدیرعامل سند موقت {v.TemporaryNumber} به‌دلیل ویرایش سند باطل شد؛ پیش از قطعی‌شدن باید دوباره تایید شود.",
                before: new { CeoApprovedByUserId = v.CeoApprovedByUserId, CeoApprovedAt = AuditNotes.Iso(v.CeoApprovedAt) },
                after: new { EditedByUserId = Me, LastEditedAt = AuditNotes.Iso(now) });
            v.CeoApprovedByUserId = null;
            v.CeoApprovedAt = null;
        }

        /// <summary>
        /// R1t D12: the user made the voucher, edited it, or caused it (a system voucher records the person whose action produced it): he cannot be its finalising manager.
        /// </summary>
        internal static bool VoucherTouched(FnVoucher v, int user) =>
            Touched(v.CreatedByUserId, v.EditorUserIds, user) || (v.TriggeredByUserId.HasValue && v.TriggeredByUserId.Value == user && user != 0);

        /// <summary>True when the voucher carries a CEO approval that is not older than its last edit (an approval from before this rule has no edit time and counts).</summary>
        internal static bool CeoApprovalIsCurrent(int? approvedBy, DateTimeOffset? approvedAt, DateTimeOffset? lastEditedAt)
        {
            if (!approvedBy.HasValue) return false;
            if (!lastEditedAt.HasValue) return true;
            return approvedAt.HasValue && approvedAt.Value > lastEditedAt.Value;
        }
    }
}
