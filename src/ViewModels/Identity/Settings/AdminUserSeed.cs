namespace ViewModels.Identity.Settings
{
    public class AdminUserSeed
    {
        /// <summary>The password that appsettings.json ships with. Outside Development the administrator is never made with it.</summary>
        public const string PlaceholderPassword = "SET-BY-INSTALL-ps1";

        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public string RoleName { get; set; }

        /// <summary>
        /// R1a2 (M7): the seeded administrator must choose a new password at the first sign-in (the installer password is known to the person who installed).
        /// Default true.
        /// </summary>
        public bool MustChangePassword { get; set; } = true;
    }
}