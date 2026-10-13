using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.Constants;
using ViewModels.NasbNiro.BuyOrderProcesses;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class ResponseBuyOrderDto : BuyOrderDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }

        public DateTimeOffset RegisterDate { get; set; }
        public DateTimeOffset InquiryDate { get; set; }
        public DateTimeOffset InquiryAcceptDate { get; set; }

        public string FinanceType { get; set; }
        public string FinanceNumber { get; set; }
        public decimal FinancePrice { get; set; }

        public IEnumerable<ResponseBuyOrderProcessesDto> BuyOrderProcesses { get; set; }
        public IEnumerable<ResponseFileDto> Files { get; set; }

        public string StatusDescription { get; set; }
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
    }
}
