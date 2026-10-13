namespace Entities.Model.NasbNiro.Contracts
{
    public enum AssemblyStatus
    {
        NewAssembly = 0,
        ReadyToAssemble = 1,
        AssemblerAssembled = 2,
        QcAcceptToFinalAssemble = 3,
        QcRejectToAssemble = 4,
        ReadyToFinalAssemble = 5,
        AssemblerFinalAssembled = 6,
        QcRejectToFinalAssemble = 7,
        PendingFinalAcceptance = 8,

        IsCompleted = 100,
    }
}
