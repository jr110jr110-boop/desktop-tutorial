using System;
using Newtonsoft.Json;

namespace ViewModels.NasbNiro.Pagination
{
    public class ContractPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }

        public DateTime? StartedFrom { get; set; }
        public DateTime? EndedAt { get; set; }

        [JsonIgnore]
        public bool ForProvider { get; set; }
    }
}
