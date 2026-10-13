namespace ViewModels.NasbNiro.ProviderDtos
{
    public class ResponseProviderDto : ProviderDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }

        public string ListName { get => EvaluationScore >= 70 ? "vendor list" : "black list"; set => value = string.Empty; }

        public ResponseProviderContactDto[] ProviderContacts { get; set; }
        public ResponseProviderAccountDto[] ProviderAccounts { get; set; }
    }
}
