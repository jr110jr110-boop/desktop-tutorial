using System;
using DataLayer.Context;
using Microsoft.Extensions.Configuration;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t D19: the three CSV channels that existed before the Sepidar outbox (the voucher file of the accounting events, the marking of events as exported or sent, and the CSV of the inventory documents).
    /// They hand a file to the finance staff and let them mark the events as "sent" without the ledger posting them, which bypasses the period locks, the maker-checker chain and the tie-out of the outbox.
    /// They stay available only as a bridge: off unless the setting <c>Sepidar:LegacyCsv</c> is true (★ false), and then for the finance manager alone; every use is written to the audit trail.
    /// </summary>
    public static class SepidarLegacy
    {
        public const string SettingKey = "Sepidar:LegacyCsv";

        /// <summary>The one role that may use the legacy channels when they are switched on.</summary>
        public const string Roles = FinanceRoles.Managers;

        public const string DisabledText = "کانال قدیمی خروجی CSV برای سپیدار غیرفعال است؛ از بسته‌های ارسال مالی (بخش ارسال به سپیدار در مالی) استفاده کنید. برای روشن کردن آن، تنظیم Sepidar:LegacyCsv را فعال کنید.";

        public const string ManagerOnlyText = "کانال قدیمی خروجی CSV سپیدار فقط برای مدیر مالی در دسترس است.";

        /// <summary>True, 1, yes or on (any case); everything else, including a missing key, is off.</summary>
        public static bool ParseFlag(string value)
        {
            string text = value?.Trim();
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1"
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Enabled(IConfiguration configuration) => ParseFlag(configuration?[SettingKey]);

        /// <summary>The Persian refusal for a call to a legacy channel, or null when it may go on.</summary>
        public static string Refusal(bool enabled, bool isFinanceManager) => !enabled ? DisabledText : !isFinanceManager ? ManagerOnlyText : null;

        /// <summary>The row of the audit trail for a use of the channel; it is saved with the next SaveChanges of the same unit of work.</summary>
        public static void RecordUse(IUnitOfWork uow, int userId, string channel) =>
            AuditNotes.Add(uow, userId, "SepidarLegacyCsv", null, "LegacyCsv", "استفاده از کانال قدیمی CSV سپیدار: " + channel, null, new { channel });
    }
}
