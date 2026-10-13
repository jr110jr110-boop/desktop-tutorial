using Entities.Model.NasbNiro.Providers;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public class ResponseProviderContactDto : ProviderContactDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }

        public string TypeDescription { get; protected set; }
        private ProviderContactType _type;
        public override ProviderContactType Type
        {
            get => this._type;
            set
            {
                this._type = value;
                switch (value)
                {
                    case ProviderContactType.Tel:
                        TypeDescription = "تلفن";
                        break;
                    case ProviderContactType.Address:
                        TypeDescription = "آدرس";
                        break;
                    case ProviderContactType.Fax:
                        TypeDescription = "فکس";
                        break;
                    case ProviderContactType.Mobile:
                        TypeDescription = "موبایل";
                        break;
                    case ProviderContactType.Email:
                        TypeDescription = "ایمیل";
                        break;
                    default:
                        TypeDescription = "نا مشخص";
                        break;
                }
            }
        }

    }
}
