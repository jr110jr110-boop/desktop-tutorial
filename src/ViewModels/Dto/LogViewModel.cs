namespace ViewModels.Dto
{
    public class LogViewModel
    {
        public int Id { set; get; }

        public string CreatedDateTime { get; set; }

        public int EventId { get; set; }

        public string Url { get; set; }

        public string LogLevel { get; set; }

        public string Logger { get; set; }

        public string Message { get; set; }

        public string StateJson { get; set; }

        public string CreatedByIp { get; set; }
        public string UserName { get; set; }
        public string FullName { get; set; }

    }
}
