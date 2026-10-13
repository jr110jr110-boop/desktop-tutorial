using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class ResponseProcessFileDto
    {
        public int ProcessId { get; set; }
        public int Id { get; set; }
        public Guid DocumentId { get; set; }
        public string Description { get; set; }
        public int Major { get; set; }
        public int Minor { get; set; }
        public string Version
        {
            get
            {
                return Major + "." + Minor;
            }
        }
        public int UserId { get; set; }
        public string UserDispalyName { get; set; }
        public DateTime RegDate { get; set; }
        public string RegDateString
        {
            get
            {
                PersianDateTime persianDate = RegDate.ToPersianDateTime();
                return persianDate.Year.ToString("0000") + "/" + persianDate.Month.ToString("00") + "/" + persianDate.Day.ToString("00");
            }
        }
    }
}
