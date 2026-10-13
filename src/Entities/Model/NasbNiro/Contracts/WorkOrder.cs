using System;
using System.Collections.Generic;
using Common.GuardToolkit;

namespace Entities.Model.NasbNiro.Contracts
{
    public class WorkOrder : BaseEntity
    {
        private DateTimeOffset? _startDate;

        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int Count { get; set; }
        public decimal WeightPercent { get; set; }

        public DateTimeOffset? StartDate
        {
            get => _startDate;
            protected set
            {
                if (value.HasValue) _startDate = value.Value.OnlyDateNotHoursUtc();
                else _startDate = null;
            }
        }


        public WorkOrderCondition Condition { get; set; }

        public virtual ICollection<AssemblyEntity> Assemblies { get; set; }
        public virtual ICollection<BuyOrder> BuyOrders { get; set; }

        public virtual ContractEntity Contract { get; set; }
        public int ContractId { get; set; }
        public int EquipmentId { get; set; }

        public void SetStartDate(DateTimeOffset date)
        {
            StartDate = date;
        }
    }
}
