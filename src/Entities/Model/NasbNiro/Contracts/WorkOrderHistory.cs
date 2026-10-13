using System;

namespace Entities.Model.NasbNiro.Contracts
{
    public class WorkOrderHistory
    {
        public int Id { get; set; }
        public int WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }
        public WorkOrderCondition Condition { get; set; }
        public DateTimeOffset Date { get; set; }
    }
}
