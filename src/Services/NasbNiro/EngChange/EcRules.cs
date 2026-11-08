using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Entities.Model.NasbNiro.EngChange;

namespace Services.NasbNiro.EngChange
{
    /// <summary>Where a production order (or a serial) stands against the effectivity point of an engineering change order.</summary>
    public enum EcPosition
    {
        /// <summary>The order states no effectivity (cannot happen for an approved order).</summary>
        NotStated = 0,

        /// <summary>Before the point: the unit keeps the old revision.</summary>
        Before = 1,

        /// <summary>At or after the point: the unit takes the new revision.</summary>
        After = 2,

        /// <summary>Planned serials on both sides of the point: the order must be split.</summary>
        Straddle = 3,
    }

    /// <summary>Pure rules of the engineering change: dispositions allowed per list, effectivity comparison, approvers, hashes. No database access, so every rule is tested alone.</summary>
    public static class EcRules
    {
        public const string RoleEngineering = "ENGINEERING";
        public const string RoleQa = "QA";
        public const string RequestRaisers = "ENGINEERING,ENGINEER,QA,QC,PMO,PRODUCTIONMANAGER,CONSTRUCTION,SHOPSUPERVISOR,PLANNING";
        public const string RequestApprovers = "ENGINEERING";
        public const string OrderMakers = "ENGINEERING,ENGINEER,PMO";
        public const string Readers = "PMO,CEO,ENGINEERING,ENGINEER,QA,QC,PLANNING,PROJECTMANAGER,PRODUCTIONMANAGER,CONSTRUCTION,SHOPSUPERVISOR,AUDITOR,DCC,COMMERCIAL,PROCUREMENT,WAREHOUSE";
        /// <summary>Told about every approved order (they are informed, they do not approve).</summary>
        public const string Informed = "PMO,CEO";

        public static string Title(EcImpactCategory c)
        {
            switch (c)
            {
                case EcImpactCategory.WhereUsed: return "کاربرد در BOM محصولات";
                case EcImpactCategory.Baseline: return "پایه پروژه‌ها";
                case EcImpactCategory.Order: return "دستورهای تولید و مسیر ساخت";
                case EcImpactCategory.Stock: return "موجودی و کار در جریان";
                case EcImpactCategory.Procurement: return "درخواست و سفارش خرید باز";
                case EcImpactCategory.QcPoint: return "نقاط کنترل کیفیت و فهرست مشخصه‌ها";
                case EcImpactCategory.SpecialProcess: return "گام‌های فرایند ویژه";
                case EcImpactCategory.Delivered: return "واحدهای تحویل‌شده";
                default: return c.ToString();
            }
        }

        public static string Title(EcDisposition d)
        {
            switch (d)
            {
                case EcDisposition.UseAsIs: return "استفاده بدون تغییر";
                case EcDisposition.Rework: return "بازکاری";
                case EcDisposition.Scrap: return "اسقاط";
                case EcDisposition.ReturnToSupplier: return "برگشت به تامین‌کننده";
                case EcDisposition.CompleteOnOldRevision: return "تکمیل با بازنگری قدیم";
                case EcDisposition.ChangeToNewRevision: return "تغییر به بازنگری جدید";
                case EcDisposition.RetrofitDeliveredUnit: return "بازسازی (رتروفیت) واحد تحویل‌شده";
                default: return d.ToString();
            }
        }

        /// <summary>The dispositions that make sense for an item of the list. A disposition outside the list is refused.</summary>
        public static IReadOnlyList<EcDisposition> Allowed(EcImpactCategory category)
        {
            switch (category)
            {
                case EcImpactCategory.WhereUsed:
                    return new[] { EcDisposition.ChangeToNewRevision, EcDisposition.CompleteOnOldRevision, EcDisposition.UseAsIs };
                case EcImpactCategory.Baseline:
                    return new[] { EcDisposition.ChangeToNewRevision, EcDisposition.CompleteOnOldRevision, EcDisposition.UseAsIs };
                case EcImpactCategory.Order:
                    return new[] { EcDisposition.ChangeToNewRevision, EcDisposition.CompleteOnOldRevision, EcDisposition.UseAsIs, EcDisposition.Rework, EcDisposition.Scrap };
                case EcImpactCategory.Stock:
                    return new[] { EcDisposition.UseAsIs, EcDisposition.Rework, EcDisposition.Scrap, EcDisposition.ReturnToSupplier, EcDisposition.CompleteOnOldRevision, EcDisposition.ChangeToNewRevision };
                case EcImpactCategory.Procurement:
                    return new[] { EcDisposition.ChangeToNewRevision, EcDisposition.UseAsIs, EcDisposition.ReturnToSupplier, EcDisposition.CompleteOnOldRevision };
                case EcImpactCategory.QcPoint:
                    return new[] { EcDisposition.ChangeToNewRevision, EcDisposition.UseAsIs };
                case EcImpactCategory.SpecialProcess:
                    return new[] { EcDisposition.ChangeToNewRevision, EcDisposition.CompleteOnOldRevision, EcDisposition.UseAsIs };
                case EcImpactCategory.Delivered:
                    return new[] { EcDisposition.RetrofitDeliveredUnit, EcDisposition.UseAsIs };
                default:
                    return new EcDisposition[0];
            }
        }

        public static bool IsAllowed(EcImpactCategory category, EcDisposition disposition) => Allowed(category).Contains(disposition);

        /// <summary>A justification is required for a disposition that leaves the item as it is or throws it away; an implementation task (owner, due date) for every disposition that needs work.</summary>
        public static bool NeedsNote(EcDisposition d) => d == EcDisposition.UseAsIs || d == EcDisposition.Scrap || d == EcDisposition.CompleteOnOldRevision;

        public static bool NeedsTask(EcDisposition d) => d != EcDisposition.UseAsIs;

        // ------------------------------------------------------------------------------------------ effectivity

        /// <summary>Splits "PO-MF-1405-00045" into the text before the last digits and the number of the digits (45).</summary>
        public static (string Prefix, decimal? Number) SplitNumber(string text)
        {
            string s = (text ?? "").Trim();
            int i = s.Length;
            while (i > 0 && s[i - 1] >= '0' && s[i - 1] <= '9') i--;
            if (i == s.Length) return (s, null);
            return (s.Substring(0, i), decimal.Parse(s.Substring(i), CultureInfo.InvariantCulture));
        }

        /// <summary>Natural order of numbers and serials: the text before the trailing digits first (ordinal, ignoring case), then the digits as a number, so 9 is before 10 and 00045 equals 45.</summary>
        public static int NaturalCompare(string a, string b)
        {
            var x = SplitNumber(a);
            var y = SplitNumber(b);
            int c = string.Compare(x.Prefix, y.Prefix, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c < 0 ? -1 : 1;
            if (x.Number.HasValue && y.Number.HasValue) return x.Number.Value.CompareTo(y.Number.Value);
            if (x.Number.HasValue) return 1;
            if (y.Number.HasValue) return -1;
            return 0;
        }

        public static bool Stated(EcOrder eco) =>
            !string.IsNullOrWhiteSpace(eco?.EffectiveFromOrderNumber) || !string.IsNullOrWhiteSpace(eco?.EffectiveFromSerial) || eco?.EffectiveFromDate != null;

        /// <summary>
        /// Where one production order stands against the order's effectivity. Every point that is stated is judged and the earliest wins: at or after ANY stated point the order takes the new revision.
        /// The planned serials of the order are compared with the serial point (planned serials on both sides: Straddle).
        /// </summary>
        public static EcPosition Position(EcOrder eco, string orderNumber, IEnumerable<string> plannedSerials, DateTime today)
        {
            if (eco == null || !Stated(eco)) return EcPosition.NotStated;
            bool after = false, before = false, straddle = false;
            if (!string.IsNullOrWhiteSpace(eco.EffectiveFromOrderNumber) && !string.IsNullOrWhiteSpace(orderNumber))
            {
                if (NaturalCompare(orderNumber, eco.EffectiveFromOrderNumber) >= 0) after = true; else before = true;
            }
            List<string> serials = (plannedSerials ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (!string.IsNullOrWhiteSpace(eco.EffectiveFromSerial) && serials.Count > 0)
            {
                int onOrAfter = serials.Count(x => NaturalCompare(x, eco.EffectiveFromSerial) >= 0);
                if (onOrAfter == serials.Count) after = true;
                else if (onOrAfter == 0) before = true;
                else straddle = true;
            }
            if (eco.EffectiveFromDate.HasValue)
            {
                if (today.Date >= eco.EffectiveFromDate.Value.Date) after = true; else before = true;
            }
            if (after) return EcPosition.After;
            if (straddle) return EcPosition.Straddle;
            return before ? EcPosition.Before : EcPosition.NotStated;
        }

        /// <summary>The position of one serial number against the serial and date points (used for the unit lists and the print).</summary>
        public static EcPosition SerialPosition(EcOrder eco, string serial, DateTime today) => Position(eco, null, new[] { serial }, today);

        public static string EffectivityText(EcOrder eco)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(eco?.EffectiveFromOrderNumber)) parts.Add("از دستور تولید " + eco.EffectiveFromOrderNumber.Trim());
            if (!string.IsNullOrWhiteSpace(eco?.EffectiveFromSerial)) parts.Add("از شماره سریال " + eco.EffectiveFromSerial.Trim());
            if (eco?.EffectiveFromDate != null) parts.Add("از تاریخ " + eco.EffectiveFromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return parts.Count == 0 ? "" : string.Join(" / ", parts);
        }

        // ------------------------------------------------------------------------------------------ parts, hashes, people

        public static string PartsKey(IEnumerable<string> parts)
        {
            List<string> list = (parts ?? new string[0]).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().ToUpperInvariant()).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
            return list.Count == 0 ? "" : "," + string.Join(",", list) + ",";
        }

        public static bool HasPart(string affectedParts, string partNumber) =>
            !string.IsNullOrWhiteSpace(affectedParts) && !string.IsNullOrWhiteSpace(partNumber) && affectedParts.Contains("," + partNumber.Trim().ToUpperInvariant() + ",", StringComparison.Ordinal);

        public static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();
        }

        public static HashSet<int> Editors(string ids) =>
            (ids ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out int n) ? n : 0).Where(n => n > 0).ToHashSet();

        public static string AddEditor(string ids, int userId)
        {
            HashSet<int> set = Editors(ids);
            if (userId > 0) set.Add(userId);
            return "," + string.Join(",", set.OrderBy(x => x)) + ",";
        }

        /// <summary>
        /// Who may give an approval of an order: neither the originator (creator) nor anybody who saved the order, never somebody who already gave the other approval (two DISTINCT persons). Returns the Persian reason or null.
        /// </summary>
        public static string ApproverProblem(int userId, int creatorId, string editorIds, int? otherApproverId)
        {
            if (userId <= 0) return "کاربر شناخته نشد.";
            if (userId == creatorId) return "سازنده دستور تغییر آن را تایید نمی‌کند.";
            if (Editors(editorIds).Contains(userId)) return "کسی که دستور تغییر یا تکلیف موارد آن را ثبت یا ویرایش کرده، آن را تایید نمی‌کند.";
            if (otherApproverId.HasValue && otherApproverId.Value == userId) return "تایید مهندسی و تایید تضمین کیفیت باید از دو نفر متفاوت باشد.";
            return null;
        }

        /// <summary>The key of an impacted item: a short prefix and the id.</summary>
        public static string Key(string prefix, params object[] ids) => prefix + ":" + string.Join(":", ids.Select(i => Convert.ToString(i, CultureInfo.InvariantCulture)));
    }
}
