namespace ViewModels.Dto
{
    public class AddressVM
    {
        public AddressVM(int id, int userProfileId, string postalCode, string cityPrefix, string remnantAddress, string alley,
            string plaque, string tel, string countryPrefix, string mobile, string emergencyTel,
            string emergencyTelCityPrefix, string emergencyTelCountryPrefix, string faxPrefix, string fax,
            string website, string email, int countryId, string countryName,
             int provinceId, string provinceName, int cityId, string cityName,
                int sectionId, string sectionName
            )
        {
            Id = id;
            PostalCode = postalCode;
            CityPrefix = cityPrefix;
            RemnantAddress = remnantAddress;
            Alley = alley;
            Plaque = plaque;
            Tel = tel;
            CountryPrefix = countryPrefix;
            Mobile = mobile;
            EmergencyTel = emergencyTel;
            EmergencyTelCityPrefix = emergencyTelCityPrefix;
            EmergencyTelCountryPrefix = emergencyTelCountryPrefix;
            FaxPrefix = faxPrefix;
            Fax = fax;
            Website = website;
            Email = email;
            UserProfileId = userProfileId;
            CountryId = countryId;
            CountryName = countryName;
            ProvinceId = provinceId;
            ProvinceName = provinceName;
            CityId = cityId;
            CityName = cityName;
            SectionId = sectionId;
            SectionName = sectionName;
        }


        public int Id { get; set; }

        public string PostalCode { get; set; }
        public string CityPrefix { get; set; }
        public string RemnantAddress { get; set; }
        public string Alley { get; set; }
        public string Plaque { get; set; }
        public string Tel { get; set; }
        public string CountryPrefix { get; set; }
        public string Mobile { get; set; }
        public string EmergencyTel { get; set; }
        public string EmergencyTelCityPrefix { get; set; }
        public string EmergencyTelCountryPrefix { get; set; }
        public string FaxPrefix { get; set; }
        public string Fax { get; set; }
        public string Website { get; set; }
        public string Email { get; set; }

        public int UserProfileId { get; set; }

        public int CountryId { get; set; }
        public string CountryName { get; set; }

        public int ProvinceId { get; set; }
        public string ProvinceName { get; set; }

        public int CityId { get; set; }
        public string CityName { get; set; }

        public int SectionId { get; set; }
        public string SectionName { get; set; }




    }
}
