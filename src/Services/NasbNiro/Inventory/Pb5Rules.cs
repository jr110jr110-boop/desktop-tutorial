using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Attachments;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Inventory
{
    /// <summary>PB5 (R29, ISO 27001 A.5.15): one place decides who sees prices, costs and amounts of the warehouse. QC, the shop floor and the guard never do.</summary>
    public static class InventoryPriceAccess
    {
        /// <summary>Values (cost, amount, running rial value): management, planning, procurement and finance roles, the same set as the purchase chain.</summary>
        public const string ValueRoles = AttachmentRoles.Prices;

        /// <summary>Unit price on a warehouse document: the storekeepers also see it, because they type it on petty-cash and opening documents. ★ company to confirm.</summary>
        public const string DocumentRoles = AttachmentRoles.Prices + ",WAREHOUSE,WHSUPERVISOR";

        public static bool SeesValues(IInventoryUserContext user) => user != null && user.IsInRole(ValueRoles);
        public static bool SeesDocumentPrices(IInventoryUserContext user) => user != null && user.IsInRole(DocumentRoles);

        public static decimal? Mask(decimal? value, bool allowed) => allowed ? value : null;

        public static void Mask(DocumentDto document, bool allowed)
        {
            if (document == null) return;
            document.PricesMasked = !allowed;
            if (allowed) return;
            foreach (DocumentLineDto line in document.Lines) line.UnitPrice = null;
        }

        public static void Mask(KardexDto kardex, bool allowed)
        {
            if (kardex == null) return;
            kardex.PricesMasked = !allowed;
            if (allowed) return;
            kardex.OpeningValue = null;
            kardex.ClosingValue = null;
            foreach (KardexRowDto row in kardex.Rows)
            {
                row.UnitPrice = null;
                row.UnitCost = null;
                row.Amount = null;
                row.RunningValue = null;
                row.Supplier = row.Supplier; // the supplier is not a price
            }
        }
    }

    /// <summary>PB5 (item 3): working days with the Iranian weekend (Friday) and the holiday tables of the company.</summary>
    public sealed class WorkCalendar
    {
        public ISet<DayOfWeek> Weekend { get; }
        public ISet<DateTime> Holidays { get; }

        public WorkCalendar(IEnumerable<DayOfWeek> weekend = null, IEnumerable<DateTime> holidays = null)
        {
            Weekend = new HashSet<DayOfWeek>(weekend == null || !weekend.Any() ? new[] { DayOfWeek.Friday } : weekend);
            Holidays = new HashSet<DateTime>((holidays ?? Enumerable.Empty<DateTime>()).Select(d => d.Date));
        }

        public bool IsWorkingDay(DateTime day) => !Weekend.Contains(day.DayOfWeek) && !Holidays.Contains(day.Date);

        /// <summary>Working days after <paramref name="from"/> up to and including <paramref name="to"/> (the day of the start does not count).</summary>
        public int Between(DateTime from, DateTime to)
        {
            DateTime a = from.Date, b = to.Date;
            if (b <= a) return 0;
            int count = 0;
            for (DateTime d = a.AddDays(1); d <= b; d = d.AddDays(1))
                if (IsWorkingDay(d)) count++;
            return count;
        }

        public DateTime AddWorkingDays(DateTime from, int days)
        {
            DateTime d = from.Date;
            int left = Math.Max(0, days);
            while (left > 0)
            {
                d = d.AddDays(1);
                if (IsWorkingDay(d)) left--;
            }
            return d;
        }

        public static async Task<WorkCalendar> LoadAsync(IUnitOfWork uow, CancellationToken ct)
        {
            List<DayOfWeek> weekend = new List<DayOfWeek>();
            List<DateTime> holidays = new List<DateTime>();
            try
            {
                weekend = (await uow.Set<HolidaysDayOfWeek>().AsNoTracking().Select(h => h.DayOfWeek).ToListAsync(ct));
                holidays = (await uow.Set<HolidaysByDate>().AsNoTracking().Select(h => h.Date).ToListAsync(ct)).Select(d => d.Date).ToList();
            }
            catch (Exception)
            {
                // the holiday tables are optional: Friday only
            }
            return new WorkCalendar(weekend, holidays);
        }
    }

    /// <summary>PB5 (item 4, F1, IAS 2.11, decision D10): freight is spread over the receipt lines by weight for metal groups and by value for the others; the spread adds up to the freight amount exactly.</summary>
    public static class FreightRules
    {
        /// <summary>Group code of the raw material (متریال). ★ the metal groups of decision D10; the company may extend the list.</summary>
        public static readonly string[] MetalGroupCodes = { "01" };

        public static bool IsMetal(string groupCode) => groupCode != null && MetalGroupCodes.Contains(groupCode.Trim());

        public sealed class LineInput
        {
            public int LineId { get; set; }
            public int ItemId { get; set; }
            public bool IsMetal { get; set; }
            public decimal? WeightKg { get; set; }
            public decimal Value { get; set; }
        }

        public sealed class Share
        {
            public int LineId { get; set; }
            public int ItemId { get; set; }
            public FreightBasis Basis { get; set; }
            public decimal BasisAmount { get; set; }
            public decimal Amount { get; set; }
        }

        /// <summary>Weight of a receipt line in kg: kg and ton units are exact, otherwise the unit weight of the item; null when it is unknown.</summary>
        public static decimal? WeightKg(string unit, decimal quantity, decimal? itemWeight)
        {
            string u = (unit ?? "").Trim().ToLowerInvariant();
            if (ProjectBalance.BalanceRules.IsWeightUnit(u)) return quantity;
            if (u == "ton" || u == "تن" || u == "tonne") return quantity * 1000m;
            if (u == "gr" || u == "g" || u == "گرم") return quantity / 1000m;
            return itemWeight.HasValue && itemWeight.Value > 0 ? itemWeight.Value * quantity : (decimal?)null;
        }

        /// <summary>Spreads whole rials: each share is rounded down and the rials left over go to the largest fractions (ties: first line), so the shares add up to the amount.</summary>
        public static List<decimal> Spread(decimal amount, IReadOnlyList<decimal> basis)
        {
            decimal total = basis.Sum();
            var result = new List<decimal>();
            if (basis.Count == 0) return result;
            if (total <= 0) throw new InvalidOperationException("basis total is zero");
            var floors = new decimal[basis.Count];
            var fractions = new decimal[basis.Count];
            decimal assigned = 0;
            for (int i = 0; i < basis.Count; i++)
            {
                decimal exact = amount * basis[i] / total;
                floors[i] = Math.Floor(exact);
                fractions[i] = exact - floors[i];
                assigned += floors[i];
            }
            int left = (int)(amount - assigned);
            foreach (int i in Enumerable.Range(0, basis.Count).OrderByDescending(i => fractions[i]).ThenBy(i => i).Take(Math.Max(0, left)))
                floors[i] += 1;
            return floors.ToList();
        }

        /// <summary>The shares of every line, or null and the Persian reason. Mixed receipts: the amount is first split between the metal lines and the others by their values, then inside each part by weight (metals) or by value.</summary>
        public static List<Share> Allocate(decimal amount, IReadOnlyList<LineInput> lines, out string error)
        {
            error = null;
            amount = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
            if (amount <= 0) { error = "مبلغ کرایه باید بیشتر از صفر باشد."; return null; }
            if (lines == null || lines.Count == 0) { error = "رسید ردیفی ندارد."; return null; }
            List<LineInput> metals = lines.Where(l => l.IsMetal).ToList();
            List<LineInput> others = lines.Where(l => !l.IsMetal).ToList();
            if (metals.Any(l => !l.WeightKg.HasValue || l.WeightKg.Value <= 0))
            { error = "وزن ردیف‌های فلزی مشخص نیست؛ واحد را کیلوگرم/تن بگذارید یا وزن واحد را در کارت کالا بنویسید."; return null; }
            if (others.Any() && others.Sum(l => l.Value) <= 0)
            { error = "ارزش ردیف‌های غیرفلزی صفر است؛ کرایه آن‌ها بر پایه ارزش پخش نمی‌شود."; return null; }

            decimal metalAmount, otherAmount;
            if (metals.Count > 0 && others.Count > 0)
            {
                decimal vm = metals.Sum(l => l.Value), vo = others.Sum(l => l.Value);
                if (vm <= 0 || vo <= 0) { error = "رسید مختلط (فلزی و غیرفلزی) برای تقسیم کرایه ارزش همه ردیف‌ها را لازم دارد."; return null; }
                metalAmount = Math.Round(amount * vm / (vm + vo), 0, MidpointRounding.AwayFromZero);
                otherAmount = amount - metalAmount;
            }
            else
            {
                metalAmount = metals.Count > 0 ? amount : 0;
                otherAmount = others.Count > 0 ? amount : 0;
            }

            var shares = new List<Share>();
            if (metals.Count > 0)
            {
                List<decimal> parts = Spread(metalAmount, metals.Select(l => l.WeightKg.Value).ToList());
                for (int i = 0; i < metals.Count; i++)
                    shares.Add(new Share { LineId = metals[i].LineId, ItemId = metals[i].ItemId, Basis = FreightBasis.Weight, BasisAmount = metals[i].WeightKg.Value, Amount = parts[i] });
            }
            if (others.Count > 0)
            {
                List<decimal> parts = Spread(otherAmount, others.Select(l => l.Value).ToList());
                for (int i = 0; i < others.Count; i++)
                    shares.Add(new Share { LineId = others[i].LineId, ItemId = others[i].ItemId, Basis = FreightBasis.Value, BasisAmount = others[i].Value, Amount = parts[i] });
            }
            return shares;
        }
    }

    /// <summary>PB5 (item 5, R26): the policy of an item is its own minimum, else the default of its group (a sub-group overrides its parent).</summary>
    public static class ReorderRules
    {
        public sealed class Policy
        {
            public decimal Min { get; set; }
            public decimal? Max { get; set; }
            public decimal? Quantity { get; set; }
            public int? LeadDays { get; set; }
            public bool FromGroup { get; set; }
        }

        public sealed class GroupDefaults
        {
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public decimal? Min { get; set; }
            public decimal? Max { get; set; }
            public decimal? Quantity { get; set; }
            public int? LeadDays { get; set; }
        }

        /// <summary>Effective defaults of every group, found by walking up the parents (the nearest group that has a value wins).</summary>
        public static Dictionary<int, GroupDefaults> Effective(IEnumerable<GroupDefaults> groups)
        {
            Dictionary<int, GroupDefaults> raw = groups.ToDictionary(g => g.Id);
            var result = new Dictionary<int, GroupDefaults>();
            foreach (GroupDefaults g in raw.Values)
            {
                var e = new GroupDefaults { Id = g.Id, ParentId = g.ParentId };
                GroupDefaults cursor = g;
                for (int guard = 0; cursor != null && guard < 50; guard++)
                {
                    e.Min ??= cursor.Min; e.Max ??= cursor.Max; e.Quantity ??= cursor.Quantity; e.LeadDays ??= cursor.LeadDays;
                    cursor = cursor.ParentId.HasValue && raw.TryGetValue(cursor.ParentId.Value, out GroupDefaults p) ? p : null;
                }
                result[g.Id] = e;
            }
            return result;
        }

        public static Policy For(decimal itemMin, decimal? itemMax, decimal? itemQuantity, int? itemLead, GroupDefaults group)
        {
            var p = new Policy { Min = itemMin, Max = itemMax, Quantity = itemQuantity, LeadDays = itemLead };
            if (group == null) return p;
            if (itemMin <= 0 && group.Min.HasValue && group.Min.Value > 0) { p.Min = group.Min.Value; p.FromGroup = true; }
            p.Max ??= group.Max;
            p.Quantity ??= group.Quantity;
            p.LeadDays ??= group.LeadDays;
            return p;
        }

        /// <summary>Reorder point = average daily use x lead time + safety stock (the minimum). Without any use it is the minimum itself. (Answers table: ROP = daily use x lead time + safety stock.)</summary>
        public static decimal ReorderPoint(decimal safety, decimal dailyUse, int leadDays) => Math.Round(Math.Max(0, dailyUse) * Math.Max(0, leadDays) + Math.Max(0, safety), 4);

        /// <summary>What to ask for: the reorder quantity of the policy, else up to the maximum, else twice the point, never less than the shortfall and never counting what is on hand or on order.</summary>
        public static decimal SuggestedQuantity(decimal rop, decimal? max, decimal? reorderQuantity, decimal onHand, decimal onOrder)
        {
            decimal position = onHand + Math.Max(0, onOrder);
            if (position > rop) return 0;
            decimal target = max.HasValue && max.Value > rop ? max.Value : rop * 2;
            decimal byTarget = Math.Max(0, target - position);
            decimal shortfall = Math.Max(0, rop - position);
            decimal wanted = reorderQuantity.HasValue && reorderQuantity.Value > 0 ? reorderQuantity.Value : byTarget;
            return Math.Round(Math.Max(wanted, shortfall), 4);
        }
    }

    /// <summary>PB5 (item 9, D-decision): stagnant stock buckets by days without movement.</summary>
    public static class StagnantRules
    {
        public const int Slow = 60, Stagnant = 90, Dead = 180;

        /// <summary>0 = moving, 1 = slow (60), 2 = stagnant (90), 3 = dead (180).</summary>
        public static int Bucket(int daysSinceMovement, int slow = Slow, int stagnant = Stagnant, int dead = Dead) =>
            daysSinceMovement >= dead ? 3 : daysSinceMovement >= stagnant ? 2 : daysSinceMovement >= slow ? 1 : 0;

        public static string Title(int bucket) => bucket == 3 ? "راکد مرده (۱۸۰ روز و بیشتر)" : bucket == 2 ? "راکد (۹۰ روز و بیشتر)" : bucket == 1 ? "کندگردش (۶۰ روز و بیشتر)" : "در گردش";
    }

    /// <summary>PB5 (item 6, F1/F3): where a consignment receipt stands against its due date.</summary>
    public static class ConsignmentRules
    {
        public const int DefaultReturnDays = 30, AlertDays1 = 7, AlertDays2 = 3;

        /// <summary>0 none, 1 = due within the first alert window (7 days), 2 = due within the second (3 days), 3 = overdue. Days left is negative when overdue.</summary>
        public static int Level(DateTime due, DateTime today, int first = AlertDays1, int second = AlertDays2)
        {
            int left = (int)(due.Date - today.Date).TotalDays;
            return left < 0 ? 3 : left <= second ? 2 : left <= first ? 1 : 0;
        }

        public static DateTime DefaultDue(DateTime documentDate, int days = DefaultReturnDays) => documentDate.Date.AddDays(Math.Max(1, days));
    }

    /// <summary>PB5 (item 8, R3): titles are compared after Persian normalisation; an equal title is a duplicate, a very similar one is a warning.</summary>
    public static class ItemTitleRules
    {
        public enum Match { None = 0, Near = 1, Exact = 2 }

        public static string Normalize(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            var sb = new StringBuilder(title.Length);
            foreach (char raw in title.Trim())
            {
                char c = raw;
                if (c == 'ي' || c == 'ى') c = 'ی';
                else if (c == 'ك') c = 'ک';
                else if (c >= '۰' && c <= '۹') c = (char)('0' + (c - '۰'));
                else if (c >= '٠' && c <= '٩') c = (char)('0' + (c - '٠'));
                if (c == '‌' || c == '‍' || c == 'ـ' || (c >= 'ً' && c <= 'ٟ')) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else sb.Append(' ');
            }
            return string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Squash(string normalized) => normalized.Replace(" ", "");

        public static int Distance(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length];
        }

        public static Match Compare(string a, string b)
        {
            string x = Squash(Normalize(a)), y = Squash(Normalize(b));
            if (x.Length == 0 || y.Length == 0) return Match.None;
            if (x == y) return Match.Exact;
            // numbers decide between two sizes (M8 and M10 are different parts): different digit sequences are never "near"
            if (Digits(a) != Digits(b)) return Match.None;
            int longest = Math.Max(x.Length, y.Length);
            double similarity = 1.0 - (double)Distance(x, y) / longest;
            if (longest >= 5 && similarity >= 0.85) return Match.Near;
            // the same words in another order
            var ta = Normalize(a).Split(' ').OrderBy(t => t, StringComparer.Ordinal).ToArray();
            var tb = Normalize(b).Split(' ').OrderBy(t => t, StringComparer.Ordinal).ToArray();
            return ta.Length > 1 && ta.SequenceEqual(tb) ? Match.Near : Match.None;
        }

        private static string Digits(string s) => new string(Normalize(s).Where(char.IsDigit).ToArray());
    }

    /// <summary>PB5 (item 13): unit conversion for the balance. An item-specific row wins over a general one; the inverse of a row is used when only the other direction is stored.</summary>
    public static class UnitConversionRules
    {
        public sealed class Row
        {
            public int? ItemId { get; set; }
            public string From { get; set; }
            public string To { get; set; }
            public decimal Factor { get; set; }
        }

        private static string Key(string unit) => (unit ?? "").Trim().ToLowerInvariant();

        /// <summary>Factor that multiplies a quantity in <paramref name="from"/> into <paramref name="to"/>; 1 when the units are the same; null when no conversion is defined.</summary>
        public static decimal? Factor(IEnumerable<Row> rows, int? itemId, string from, string to)
        {
            string a = Key(from), b = Key(to);
            if (a.Length == 0 || b.Length == 0 || a == b) return 1m;
            if (rows == null) return null;
            List<Row> list = rows.Where(r => r.Factor > 0).ToList();
            foreach (int? scope in new int?[] { itemId, null })
            {
                if (scope == null && itemId == null) { }
                Row direct = list.FirstOrDefault(r => r.ItemId == scope && Key(r.From) == a && Key(r.To) == b);
                if (direct != null) return direct.Factor;
                Row inverse = list.FirstOrDefault(r => r.ItemId == scope && Key(r.From) == b && Key(r.To) == a);
                if (inverse != null) return 1m / inverse.Factor;
            }
            return null;
        }

        public static async Task<List<Row>> LoadAsync(IUnitOfWork uow, CancellationToken ct) =>
            await uow.Set<ItemUnitConversion>().AsNoTracking().Where(c => !c.Deleted && c.IsActive)
                .Select(c => new Row { ItemId = c.ItemId, From = c.FromUnit, To = c.ToUnit, Factor = c.Factor }).ToListAsync(ct);
    }
}
