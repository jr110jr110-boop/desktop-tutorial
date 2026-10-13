namespace Entities.Model.NasbNiro.Contracts
{
    public enum BuyOrderStatus
    {
        NewBuyOrder = 0,
        InquiriesAdded = 1,
        TechnicalScoreAdded = 3,
        WinnerIsSelected = 4,
        FinanceDocumentSetted = 5,
        TempReceiptGenerated = 6,
        QcAccepted = 7,
        QcRejected = 8,
        EngineeringAccepted = 9,

        // after 5
        CommercialBought = 10,
        NeedsCommercialFixes = 20,

        NewBuyOrderForAssembler = 11,
        AssemblerAssembled = 12,
        NeedsAssemblerFixes = 13,
        NewBuyOrderForConstructor = 14,
        ConstructorConstructed = 15,
        NeedsConstractorFixes = 16,

        RejectedGivenToSalvage = 90,
        RejectedAcceptedByPmo = 91,
        RejectedAccepted = 92,
        FinanceAcceptedGivenToWarehouse = 99,
        CompletedWarehouseFinallyAccepted = 100
    }
}