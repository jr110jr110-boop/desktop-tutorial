using System;
using System.ComponentModel.DataAnnotations;
using Common.GuardToolkit;

namespace ViewModels.NasbNiro.WorkOrderDtos
{
    public class UpdateWorkOrderSetStartDateDto
    {
        private DateTimeOffset _startDate;

        [Required]
        public int WorkOrderId { get; set; }

        [Required]
        public DateTimeOffset StartDate
        {
            get => _startDate;
            set
            {
                _startDate = value.OnlyDateNotHoursUtc();
            }
        }
    }
}
