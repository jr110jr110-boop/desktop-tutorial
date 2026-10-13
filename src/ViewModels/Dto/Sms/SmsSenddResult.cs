using System.Collections.Generic;

namespace ViewModels.Dto.Sms
{
    public class Result
    {
        public int status { get; set; }
    }
    public class Entries
    {
        public int cost { get; set; }
    }
    public class SmsSenddResult
    {
        public Result Return { get; set; }
        public List<Entries> entries { get; set; }
    }
}
