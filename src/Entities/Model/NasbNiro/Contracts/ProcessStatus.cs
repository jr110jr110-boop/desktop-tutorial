namespace Entities.Model.NasbNiro.Contracts
{
    public enum ProcessStatus
    {
        NewProcess = 0,
        PmoAccepted = 1,
        EngineeringWorkflowStart = 2,
        DocumentsAdded = 3,
        EngineerAcceptedDocuments = 4,
        MasterShouldAccept = 5,
        ReadyToGo = 6,
        BuyOrderHasBeenAdded = 7,
        ProcessIsCompleted = 100,
    }
}
