using System;
using Microsoft.AspNetCore.Identity;

namespace ViewModels.Identity.Settings
{
    public class SiteSettings
    {
        public AdminUserSeed AdminUserSeed { get; set; }
        public Logging Logging { get; set; }
        public SmtpConfig Smtp { get; set; }
        public Connectionstrings ConnectionStrings { get; set; }
        public bool EnableEmailConfirmation { get; set; }
        public TimeSpan EmailConfirmationTokenProviderLifespan { get; set; }
        public int NotAllowedPreviouslyUsedPasswords { get; set; }
        public int ChangePasswordReminderDays { get; set; }
        public PasswordOptions PasswordOptions { get; set; }
        public ActiveDatabase ActiveDatabase { get; set; }
        public FileManagerSettings FileManager { get; set; }
        public string UsersAvatarsFolder { get; set; }
        public string UsersAvatarsApiBase { get; set; }
        public string UserDefaultPhoto { get; set; }
        public string ContentSecurityPolicyErrorLogUri { get; set; }
        public CookieOptions CookieOptions { get; set; }
        public LockoutOptions LockoutOptions { get; set; }
        public UserAvatarImageOptions UserAvatarImageOptions { get; set; }
        public string[] EmailsBanList { get; set; }
        public string[] PasswordsBanList { get; set; }
        public string Secret { get; set; }
        public string SiteTitle { get; set; }
        public string FcmSenderId { get; set; }
        public string FcmServerKey { get; set; }
        public ApiSettings ApiSettings { get; set; }

        /// <summary>R1a2: approver rule, rate limit, audit key, temporary-password lifetime and the one-time forced password change (section "Identity").</summary>
        public IdentitySecurityOptions Identity { get; set; } = new IdentitySecurityOptions();
    }
}