using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.Constants;

namespace ViewModels.NasbNiro.AssemblyDtos
{
    public class ResponseAssemblyDto : AssemblyDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }
        private AssemblyStatus _status { get; set; }
        public string StatusDescription { get; set; }
        public int? ParentId { get; set; }
        public IEnumerable<ResponseAssemblyDto> Children { get; set; }
        public DateTimeOffset? StartDate { get; set; }
        public DateTimeOffset? EndDate { get; set; }
        public DateTimeOffset? ActualStartDate { get; set; }
        public DateTimeOffset? ActualEndDate { get; set; }
        public decimal TotalWeightPercent { get; set; }

        public AssemblyStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                StatusDescription = AssemblyStatusDescriptions.GetProperDescription(value);
            }
        }
    }
}
