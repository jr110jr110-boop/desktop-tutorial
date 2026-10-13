namespace Entities.Model.NasbNiro.Contracts
{
    public class BuyOrderProcesses
    {
        public string Attribute { get; set; }

        public int BuyOrderId { get; set; }
        public BuyOrder BuyOrder { get; set; }

        public int ProcessId { get; set; }
        public ProcessEntity Process { get; set; }
    }
}
