using System;
using System.Collections.Generic;
using System.Linq;

namespace Entities.Model.NasbNiro.Contracts
{
    public class BuyOrder : BaseEntity
    {
        public string ExitCommodityCode { get; set; }
        public string EnterCommodityCode { get; set; }
        public int Count { get; set; }
        public string Title { get; set; }

        public DateTimeOffset RegisterDate { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset InquiryDate { get; set; }
        public DateTimeOffset InquiryAcceptDate { get; set; }
        public DateTimeOffset FinanceDocDate { get; set; }
        public DateTimeOffset FinanceAcceptDate { get; set; }
        public DateTimeOffset TempReceiveDate { get; set; }
        public DateTimeOffset ReceiveDate { get; set; }

        public BuyOrderStatus Status { get; protected set; }

        public string FinanceType { get; set; }
        public string FinanceNumber { get; set; }
        public decimal FinancePrice { get; set; }

        public string TempReceiptCode { get; set; }
        public string ReceiptCode { get; set; }

        public int BuyCount { get; set; }
        public int ReserveCount { get; set; }
        public int RealBuyCount { get; set; }
        public Guid LogShitId { get; set; }

        public int MachineId { get; set; }
        public Guid TimeSheet { get; set; }
        public string ConstractorDescription { get; set; }

        public int WorkOrderId { get; set; }
        public virtual WorkOrder WorkOrder { get; set; }

        public virtual ICollection<BuyOrderProcesses> BuyOrderProcesses { get; set; }
        public virtual ICollection<BuyOrderFile> BuyOrderFiles { get; set; }
        public virtual ICollection<Inquiry> Inquiries { get; set; }

        public bool CanAddInquiries()
        {
            return this.Status == BuyOrderStatus.NewBuyOrder;
        }

        public void InquiriesAdded(string financeType)
        {
            this.FinanceType = financeType;
            this.Status = BuyOrderStatus.InquiriesAdded;
            this.InquiryDate = DateTimeOffset.UtcNow;
        }


        public bool CanAddTechnicalScore()
        {
            return this.Status == BuyOrderStatus.InquiriesAdded;
        }

        public void TechnicalScoreAdded()
        {
            this.Status = BuyOrderStatus.TechnicalScoreAdded;
        }

        public bool CanSelectWinner()
        {
            return this.Status == BuyOrderStatus.TechnicalScoreAdded;
        }

        public void WinnerSelected()
        {
            this.Status = BuyOrderStatus.WinnerIsSelected;
        }

        public bool CanSetFinanceDocuments()
        {
            return this.Status == BuyOrderStatus.WinnerIsSelected;
        }

        public void FinanceDocumentSetted(DateTimeOffset? optionalFinanceDocDate)
        {
            this.Status = BuyOrderStatus.FinanceDocumentSetted;

            this.FinanceDocDate = optionalFinanceDocDate.HasValue ?
                optionalFinanceDocDate.Value :
                DateTimeOffset.UtcNow;
        }

        public bool CanFinanceAccept()
        {
            return this.Status == BuyOrderStatus.QcAccepted ||
                this.Status == BuyOrderStatus.EngineeringAccepted;
        }

        public void FinanceAccepted()
        {
            this.Status = BuyOrderStatus.FinanceAcceptedGivenToWarehouse;
            this.FinanceAcceptDate = DateTimeOffset.UtcNow;
        }

        public bool CanAddTempReceipt()
        {
            //return BuyOrderHelper.StatusesForAddingTempReceipt.Contains(this.Status);
            return this.Status == BuyOrderStatus.CommercialBought || Status == BuyOrderStatus.AssemblerAssembled || Status == BuyOrderStatus.ConstructorConstructed;
        }

        public void AddTempReciept(Guid logShitId)
        {
            this.Status = BuyOrderStatus.TempReceiptGenerated;
            this.TempReceiveDate = DateTimeOffset.UtcNow;
            this.LogShitId = logShitId;
            //TempReceiptCode = tempReceiptCode;
        }

        public void RevertStatus(BuyOrderStatus buyOrderStatus)
        {
            this.Status = buyOrderStatus;
        }

        public void NewForAssembler()
        {
            this.Status = BuyOrderStatus.NewBuyOrderForAssembler;
        }

        public void NewForConstructor()
        {
            this.Status = BuyOrderStatus.NewBuyOrderForConstructor;
        }

        public bool CanQcAcceptOrReject()
        {
            return this.Status == BuyOrderStatus.TempReceiptGenerated;
        }

        public void QcAccepted()
        {
            this.Status = BuyOrderStatus.QcAccepted;
        }

        public void QcReject()
        {
            this.Status = BuyOrderStatus.QcRejected;
        }

        public void EngineeringAccept()
        {
            this.Status = BuyOrderStatus.EngineeringAccepted;
        }

        public void EngineeringReject()
        {
            this.Status = BuyOrderStatus.RejectedGivenToSalvage;
        }

        public void PmoAcceptReject()
        {
            this.Status = BuyOrderStatus.RejectedAcceptedByPmo;
        }

        public void Rejected()
        {
            this.Status = BuyOrderStatus.RejectedAccepted;
        }

        public bool CanCommercialBuy()
        {
            return Status == BuyOrderStatus.FinanceDocumentSetted || Status == BuyOrderStatus.NeedsCommercialFixes;
        }

        public void CommercialBought(int realBuyCount)
        {
            Status = BuyOrderStatus.CommercialBought;
            RealBuyCount = realBuyCount;
        }

        public bool CanAssemblerAssemble()
        {
            return this.Status == BuyOrderStatus.NewBuyOrderForAssembler || this.Status == BuyOrderStatus.NeedsAssemblerFixes;
        }

        public bool CanConstructorConstructe()
        {
            return this.Status == BuyOrderStatus.NewBuyOrderForConstructor || this.Status == BuyOrderStatus.NeedsConstractorFixes;
        }

        public void AssemblerAssembled()
        {
            this.Status = BuyOrderStatus.AssemblerAssembled;
        }

        public void ConstructorConstructed()
        {
            this.Status = BuyOrderStatus.ConstructorConstructed;
        }

        public bool CanWarehouseFinallyAccept()
        {
            return BuyOrderHelper.StatusesForAddingFinalReceipt.Contains(Status);
        }

        public void WarehouseFinallyAccepted()
        {
            this.Status = BuyOrderStatus.CompletedWarehouseFinallyAccepted;
            //ReceiptCode = receiptCode;
        }

        public bool CanEnginneringAcceptOrRejectOrSayNeedsFixes()
        {
            return this.Status == BuyOrderStatus.QcRejected;
        }

        public bool CanAcceptReject()
        {
            return this.Status == BuyOrderStatus.RejectedGivenToSalvage;
        }

        public bool CanReject()
        {
            return this.Status == BuyOrderStatus.RejectedAcceptedByPmo;
        }

        public void EngineeringSaysNeedsFixes()
        {
            this.Status = BuyOrderStatus.NeedsCommercialFixes;
        }

        public void EngineeringSaysNeedsAssemblerFixes()
        {
            this.Status = BuyOrderStatus.NeedsAssemblerFixes;
        }

        public void EngineeringSaysNeedsContractorFixes()
        {
            this.Status = BuyOrderStatus.NeedsConstractorFixes;
        }


        public static class BuyOrderHelper
        {
            public static IEnumerable<BuyOrderStatus> StatusesForAddingTempReceipt => new BuyOrderStatus[]
            {
                BuyOrderStatus.CommercialBought ,
                BuyOrderStatus.AssemblerAssembled
            };

            public static IEnumerable<BuyOrderStatus> StatusesForAddingFinalReceipt => new BuyOrderStatus[]
            {
                BuyOrderStatus.FinanceAcceptedGivenToWarehouse
            };
        }
    }
}
