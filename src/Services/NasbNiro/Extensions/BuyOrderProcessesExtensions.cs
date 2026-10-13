using ViewModels.NasbNiro.BuyOrderProcesses;

namespace Services.NasbNiro.Extensions
{
    public static class BuyOrderProcessesExtensions
    {
        public static GetBuyOrderProcessesDto AsGetBuyOrderProcessesDto(this BuyOrderProcessesDtoKey buyOrderProcessesDtoKey)
        {
            return new GetBuyOrderProcessesDto()
            {
                BuyOrderId = buyOrderProcessesDtoKey.BuyOrderId,
                ProcessId = buyOrderProcessesDtoKey.ProcessId
            };
        }
    }
}
