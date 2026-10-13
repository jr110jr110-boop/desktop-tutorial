namespace ViewModels.Identity.Settings
{
    public class ApiSettings
    {
        public string LoginPath { get; set; }
        public string LogoutPath { get; set; }
        public string RefreshTokenPath { get; set; }
        public string AccessTokenObjectKey { get; set; }
        public string RefreshTokenObjectKey { get; set; }
        public string AdminRoleName { get; set; }
        public string ConfirmPhonePath { get; set; }
        public string RegisterPath { get; set; }
    }
}