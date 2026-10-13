namespace ViewModels.NasbNiro.ProviderDtos
{
    public class AddProviderDto : ProviderDtoBase
    {
        public AddProviderContactDto[] ProviderContacts { get; set; }
        public AddProviderAccountDto[] ProviderAccounts { get; set; }
    }
}
