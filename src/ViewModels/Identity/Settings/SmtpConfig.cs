namespace ViewModels.Identity.Settings
{
    /// <summary>
    /// SMTP settings bound from the "Smtp" section of appsettings.json
    /// (same shape as the class that used to come from DNTCommon.Web.Core).
    /// </summary>
    public class SmtpConfig
    {
        public string Server { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int Port { get; set; }
        public bool EnableSsl { get; set; }
        public string LocalDomain { get; set; }
        public bool UsePickupFolder { get; set; }
        public string PickupFolder { get; set; }
        public string FromName { get; set; }
        public string FromAddress { get; set; }
    }
}
