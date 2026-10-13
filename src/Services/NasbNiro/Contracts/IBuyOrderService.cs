using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IBuyOrderService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseBuyOrderDto>> AddBuyOrderAsync(
            AddBuyOrderDto addBuyOrderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderAsync(
            UpdateBuyOrderDto updateBuyOrderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderRevertStatusAsync(
            RevertBuyOrderDto revertBuyOrderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderFullParametersAsync(
            UpdateBuyOrderFullParametersDto updateBuyOrderFullParametersDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderInquiriesAddedAsync(
            UpdateBuyOrderInquiriesAddedDto updateBuyOrderInquiriesAddedDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderTechnicalScoreAddedAsync(
            UpdateBuyOrderTechnicalScoreAddedDto updateBuyOrderTechnicalScoreAddedDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderWinnerSelectedAsync(
            UpdateBuyOrderWinnerSelectedDto updateBuyOrderWinnerSelectedDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderFinanceDocSettedAsync(
                UpdateBuyOrderFinanceDocumentSettedDto updateBuyOrderFinanceDocumentSettedDto, CancellationToken cancellationToken);
        Task<ResponseDto<ResponseBuyOrderDto>> CommercialBoughtAsync(
            UpdateBuyOrderCommercialBoughtDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderTempReceiptAddedAsync(
            UpdateBuyOrderTempReceiptAddedDto updateBuyOrderTempReceiptAddedDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderQcAcceptAsync(
            UpdateBuyOrderQcAcceptDto updateBuyOrderQcAcceptDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderQcRejectAsync(
            UpdateBuyOrderQcRejectDto updateBuyOrderQcRejectDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderEngineeringAcceptedAsync(
            UpdateBuyOrderEngineeringAcceptedDto updateBuyOrderEngineeringAccepted, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderEngineeringRejectAsync(
            UpdateBuyOrderEngineeringRejectDto updateBuyOrderEngineeringRejectDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderPmoAcceptRejectAsync(
            UpdateBuyOrderEngineeringRejectDto updateBuyOrderEngineeringRejectDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderRejectedAsync(
            UpdateBuyOrderEngineeringRejectDto updateBuyOrderEngineeringRejectDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderFinanceAcceptedAsync(
            UpdateBuyOrderFinanceAcceptedDto updateBuyOrderFinanceAcceptedDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> DeleteBuyOrderAsync(
            int buyOrderId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteBuyOrderResultDto>>> DeleteBuyOrderCollectionAsync(
            IEnumerable<int> buyOrderIds, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseBuyOrderDto>>> GetAllResponseBuyOrderDtosAsync(
            BuyOrderPaginationParams buyOrderPaginationParams, CancellationToken cancellationToken);
        Task<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderAssemblerAssembledAsync(
            UpdateBuyOrderAssemblerAssembled dto, CancellationToken cancellationToken);
        Task<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderConstructorConstructedAsync(
            UpdateBuyOrderConstructorConstructed dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<ResponseBuyOrderAndProcesses>>> GetBuyOrdersAndProcessesAsync(
            int workOrderId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> GetResponseBuyOrderDtoAsync(
            GetBuyOrderDto getBuyOrderDto, CancellationToken cancellationToken);
        ValueTask<BuyOrder> GetBuyOrderAsync(
            GetBuyOrderDto getBuyOrderDto, CancellationToken cancellationToken, bool includeInquiries = false);
        ValueTask<bool> BuyOrderExistsAsync(
            int buyOrderId, CancellationToken cancellationToken, Expression<Func<BuyOrder, bool>> where = null);
        ValueTask<ResponseDto<string>> AttachFileToBuyOrderAsync(
            AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<bool>> DetachFileFromBuyOrderAsync(
            int buyOrderId, Guid documentId, CancellationToken cancellationToken);
        Task<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderWarehouseFinallyAcceptedAsync(
            UpdateBuyOrderWarehouseFinallyAccepted dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderEngineeringSaysNeedsFixesAsync(
            UpdateBuyOrderEngineeringSaysNeedsFixedsDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseUpdateBuyOrderChangeCountDto>> GetBuyOrderChangedCountsAsync(
            UpdateBuyOrderChangeCountDto dto,
            CancellationToken cancellationToken);
    }
}
