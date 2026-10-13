namespace ViewModels.NasbNiro.ProcessDtos
{
    public class ResponseProcessDashboardDto
    {
        public int Id { get; set; }
        public int BomId { get; set; }
        public string Title { get; set; }

        public int MapDuration { get; set; }
        public int MasterDuration { get; set; }
        public int ProcessDuration { get; set; }

        public int TotalDuration
        {
            get => this.MapDuration + this.MasterDuration + this.ProcessDuration;
        }
    }
}
