namespace ViewModels.Identity.Settings
{
    /// <summary>
    /// R1a2 (second security review): the settings of the sign-in and administration rules. Section "Identity" of the settings file
    /// (environment variables: Identity__ApproverMinDays and so on). Every value has a default; ★ = provisional decision.
    /// </summary>
    public class IdentitySecurityOptions
    {
        /// <summary>
        /// ★ A PMO user must have held the PMO role for this many days before he or she may approve a sensitive role or a password reset of somebody else
        /// (a PMO account made a minute ago cannot be the second pair of eyes). 0 switches the rule off (the CI smoke does this: its second PMO is made seconds before it approves).
        /// </summary>
        public int ApproverMinDays { get; set; } = 1;

        /// <summary>★ Sign-in, refresh, change-temporary-password and logout calls allowed per client address per minute (fixed window); more are answered with HTTP 429.</summary>
        public int RateLimitPerMinute { get; set; } = 20;

        /// <summary>
        /// Secret key of the keyed hash (HMAC-SHA256) that the audit trail writes next to the masked national code and phone number, so a change that keeps the
        /// last four digits is still detectable but the value cannot be guessed back. install.ps1 makes one at the first install. Empty: no hash is written, only the masked text.
        /// </summary>
        public string AuditHmacKey { get; set; }

        /// <summary>★ A temporary password that a PMO user set for somebody stops working after this many hours (the user must ask for a new one).</summary>
        public int TemporaryPasswordHours { get; set; } = 24;

        /// <summary>
        /// ★ On the first start after an upgrade every user (not the AI seats) who has a password gets the "must change the password" flag once, because the passwords
        /// chosen before the new password rule are unknown to us. The audit trail row "UpgradeForced" is the marker that it ran; set false to skip it.
        /// </summary>
        public bool ForcePasswordChangeAtUpgrade { get; set; } = true;

        /// <summary>
        /// ★ R1a2 (L-5): on the first start after an upgrade every PMO user (not the AI seats) who has no record of how he got the role gets a "Legacy" record (who gave the role is not known),
        /// and the audit trail gets a row "LegacyPmoRecord" for each of them and one summary row, which is the marker that it ran; set false to skip it. The company should review the
        /// list of PMO users once after this (the audit rows list them).
        /// </summary>
        public bool LegacyPmoRecordsAtUpgrade { get; set; } = true;
    }
}
