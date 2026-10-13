using System.ComponentModel.DataAnnotations.Schema;
using Entities.Identity;

namespace Entities.Model
{
    public class Address
    {
        public Address()
        {

        }
        public Address(int userProfileId, string postalCode, string cityPrefix, string remnantAddress, string alley, string plaque, string tel, string countryPrefix, string mobile, string emergencyTel, string emergencyTelCityPrefix, string emergencyTelCountryPrefix, string faxPrefix, string fax, string website, string email)
        {
            Update(userProfileId, postalCode, cityPrefix, remnantAddress, alley, plaque, tel, countryPrefix, mobile, emergencyTel, emergencyTelCityPrefix, emergencyTelCountryPrefix, faxPrefix, fax, website, email);
        }

        public void Update(int userProfileId, string postalCode, string cityPrefix, string remnantAddress, string alley, string plaque, string tel, string countryPrefix, string mobile, string emergencyTel, string emergencyTelCityPrefix, string emergencyTelCountryPrefix, string faxPrefix, string fax, string website, string email)
        {
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
        [ForeignKey("UserProfileId")]
        public virtual UserProfile UserProfile { get; set; }


        public virtual Country Country { get; set; }
        public virtual Province Province { get; set; }
        public virtual City City { get; set; }
        public virtual Section Section { get; set; }
    }
}
