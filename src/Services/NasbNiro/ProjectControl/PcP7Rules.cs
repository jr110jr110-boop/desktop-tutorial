using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>Pure rules of the P-B7a registers and cartables (no database), so the tests can check them directly.</summary>
    public static class PcDailyRules
    {
        public static List<int> Editors(string csv) =>
            string.IsNullOrWhiteSpace(csv) ? new List<int>() : csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : 0).Where(v => v > 0).ToList();

        public static string AddEditor(string csv, int userId)
        {
            List<int> list = Editors(csv);
            if (!list.Contains(userId)) list.Add(userId);
            return "," + string.Join(",", list) + ",";
        }

        /// <summary>The person who created or edited the daily report does not confirm it.</summary>
        public static bool MayConfirm(int creatorUserId, string editorUserIds, int userId) => userId > 0 && userId != creatorUserId && !Editors(editorUserIds).Contains(userId);
    }

    public static class PcRiskRules
    {
        public static bool ValidScale(int v) => v >= 1 && v <= 5;

        public static int Score(int probability, int impact) => probability * impact;

        /// <summary>low below the high limit, medium up to it, high from it, critical from the critical limit.</summary>
        public static string Level(int score, int highScore, int criticalScore) =>
            score >= criticalScore ? "critical" : score >= highScore ? "high" : score >= Math.Max(1, highScore / 2) ? "medium" : "low";

        public static string LevelText(string level) => level == "critical" ? "بحرانی" : level == "high" ? "زیاد" : level == "medium" ? "متوسط" : "کم";
    }

    public static class PcStakeholderRules
    {
        /// <summary>The power / interest grid: high influence and high interest are 4..5 (3 is the middle and counts as high).</summary>
        public static string Quadrant(int influence, int interest)
        {
            bool power = influence >= 3, care = interest >= 3;
            return power && care ? "manage-closely" : power ? "keep-satisfied" : care ? "keep-informed" : "monitor";
        }

        public static string QuadrantText(string q) => q == "manage-closely" ? "مدیریت نزدیک" : q == "keep-satisfied" ? "راضی نگه‌داشتن" : q == "keep-informed" ? "مطلع نگه‌داشتن" : "پایش";
    }

    public static class PcEvaluationRules
    {
        /// <summary>The weighted score out of 100: sum(weight x score / 5) / sum(weight).</summary>
        public static decimal Total(IEnumerable<(decimal Weight, decimal Score)> rows)
        {
            List<(decimal Weight, decimal Score)> list = rows.ToList();
            decimal weights = list.Sum(r => r.Weight);
            if (weights <= 0m) return 0m;
            return Math.Round(list.Sum(r => r.Weight * r.Score / 5m) / weights * 100m, 2, MidpointRounding.AwayFromZero);
        }

        public static string Grade(decimal total, decimal a, decimal b, decimal c) => total >= a ? "A" : total >= b ? "B" : total >= c ? "C" : "D";

        public static readonly (string Criterion, decimal Weight)[] ContractorCriteria =
        {
            ("کیفیت اجرای کار", 25m), ("رعایت زمان‌بندی", 20m), ("رعایت ایمنی و بهداشت (HSE)", 15m), ("مدیریت نیروی انسانی و تجهیزات", 10m),
            ("پاسخگویی و هماهنگی", 10m), ("مستندسازی و گزارش‌دهی", 10m), ("رعایت شرایط مالی و قراردادی", 10m),
        };

        public static readonly (string Criterion, decimal Weight)[] ConsultantCriteria =
        {
            ("کیفیت و کامل‌بودن مدارک", 25m), ("رعایت زمان تحویل", 20m), ("دقت فنی و صحت محاسبات", 20m), ("پاسخگویی به ملاحظات", 15m),
            ("هماهنگی با سایر رشته‌ها", 10m), ("رعایت استانداردها", 10m),
        };
    }

    public static class PcPhaseRules
    {
        /// <summary>Average of the four 1..5 scores, 0..5 with two decimals.</summary>
        public static decimal Overall(int schedule, int cost, int quality, int scope) => Math.Round((schedule + cost + quality + scope) / 4m, 2, MidpointRounding.AwayFromZero);

        public static bool ValidScore(int v) => v >= 1 && v <= 5;
    }

    public static class PcProgressChainRules
    {
        public static readonly string[] AllowedRoles = { "PROJECTMANAGER", "PLANNING", "PMO", "CEO", "COMMERCIAL", "ENGINEERING", "CONSTRUCTION", "QC", "FINANCIAL" };

        /// <summary>The problem with a chain of levels, or null. At least one and at most 6 levels; each is a role or a position; the last level is the PMO or planning (the person who writes the value to the plan).</summary>
        public static string Problem(IList<(string Role, int? PositionId)> levels)
        {
            if (levels == null || levels.Count == 0) return "زنجیره تایید دست‌کم یک سطح دارد.";
            if (levels.Count > 6) return "زنجیره تایید حداکثر ۶ سطح دارد.";
            for (int i = 0; i < levels.Count; i++)
            {
                string role = string.IsNullOrWhiteSpace(levels[i].Role) ? null : levels[i].Role.Trim().ToUpperInvariant();
                if ((role != null) == levels[i].PositionId.HasValue) return "سطح " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": فقط یکی از «نقش» یا «سمت» را مشخص کنید.";
                if (role != null && Array.IndexOf(AllowedRoles, role) < 0) return "سطح " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": نقش نامعتبر است.";
            }
            // the same role or position twice in a row would make one person the whole chain
            for (int i = 1; i < levels.Count; i++)
            {
                string a = levels[i - 1].Role?.Trim().ToUpperInvariant(), b = levels[i].Role?.Trim().ToUpperInvariant();
                if ((a != null && a == b) || (a == null && b == null && levels[i - 1].PositionId == levels[i].PositionId)) return "سطح " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": نقش یا سمت پشت‌سرهم تکراری است؛ هر سطح باید شخص دیگری باشد.";
            }
            string last = levels[levels.Count - 1].Role?.Trim().ToUpperInvariant();
            if (last != "PMO" && last != "PLANNING") return "آخرین سطح زنجیره باید PMO یا برنامه‌ریزی باشد؛ مقدار تاییدشده را او در برنامه می‌نویسد.";
            return null;
        }

        public static IList<(string Title, string Role)> DefaultChain() => new List<(string, string)> { ("مدیر پروژه", "PROJECTMANAGER"), ("PMO", "PMO") };
    }

    /// <summary>
    /// Who may link a task to a WBS activity, change the weight (estimated hours) of such a task, or report progress of the activity: planning and the PMO, the unit manager of the activity (the holder of its responsible-unit role)
    /// and the project manager of that contract. Nobody else, so a normal user cannot reshape the percent of an activity.
    /// </summary>
    public static class PcActivityAuthority
    {
        public static async Task<bool> MayAsync(IUnitOfWork uow, IInventoryUserContext user, int activityId, CancellationToken ct)
        {
            if (user.IsInRole("PMO,PLANNING")) return true;
            var a = await uow.Set<PcActivity>().AsNoTracking().Where(x => x.Id == activityId && !x.Deleted).Select(x => new { x.ContractId, x.ResponsibleUnit }).FirstOrDefaultAsync(ct);
            if (a == null) return false;
            if (!string.IsNullOrWhiteSpace(a.ResponsibleUnit) && user.IsInRole(a.ResponsibleUnit)) return true;
            return await IsProjectManagerAsync(uow, user, a.ContractId, ct);
        }

        /// <summary>The project manager recorded in the terms of the contract.</summary>
        public static async Task<bool> IsProjectManagerAsync(IUnitOfWork uow, IInventoryUserContext user, int contractId, CancellationToken ct)
        {
            int me = user.UserId;
            if (me <= 0) return false;
            return await uow.Set<PcContractTerms>().AsNoTracking().AnyAsync(t => t.ContractId == contractId && !t.Deleted && t.ProjectManagerUserId == me, ct);
        }
    }
}
