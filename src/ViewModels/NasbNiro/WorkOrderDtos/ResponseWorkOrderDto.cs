using System;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.WorkOrderDtos
{
    public class ResponseWorkOrderDto : WorkOrderDtoBase
    {
        private WorkOrderCondition _condition;

        public int Id { get; set; }
        public bool Deleted { get; set; }
        public int EquipmentId { get; set; }
        public DateTimeOffset? StartDate { get; set; }
        public string ConditionDescription { get; protected set; }
        public WorkOrderCondition Condition
        {
            get => _condition;
            set
            {
                _condition = value;
                this.ConditionDescription = GetConditionDescription(value);
            }
        }

        private string GetConditionDescription(WorkOrderCondition value)
        {
            switch (value)
            {
                case WorkOrderCondition.Active:
                    return "فعال";
                case WorkOrderCondition.Stopped:
                    return "تعلیق شده";
                case WorkOrderCondition.Done:
                    return "به اتمام رسیده";
                case WorkOrderCondition.Canceled:
                    return "لغو شده";
                default:
                    return "وضعیت پشتیبانی نمیشود.";
            }
        }
    }
}
