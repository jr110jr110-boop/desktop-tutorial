using System.Collections.Generic;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.Constants;
using ViewModels.NasbNiro.ProcessDtos;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class ResponseBuyOrderCardBoardDto
    {
        public string WorkOrderTitle { get; set; }
        public string ExitCommodityCode { get; set; }
        public string EnterCommodityCode { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
        public int ReserveCount { get; set; }
        public int BuyCount { get; set; }
        public int RealBuyCount { get; set; }
        public int WorkOrderId { get; set; }
        public int Id { get; set; }
        public bool Deleted { get; set; }
        public string FinanceType { get; set; }
        public string FinanceNumber { get; set; }
        /// <summary>R1a2 (M8): null for a user who is not allowed to see prices.</summary>
        public decimal? FinancePrice { get; set; }

        public string StatusDescription { get; protected set; }
        private BuyOrderStatus _status { get; set; }
        public BuyOrderStatus Status
        {
            get => this._status;
            set
            {
                this._status = value;
                StatusDescription = BuyOrderStatusDescriptions.GetStatusDescription(value);
            }
        }

        public IEnumerable<ResponseProcessDto> Processes { get; set; }
    }
}
