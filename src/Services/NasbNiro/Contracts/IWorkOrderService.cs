using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.WorkOrderDtos;

namespace Services.NasbNiro.WorkOrders
{
    public interface IWorkOrderService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseWorkOrderDto>> AddWorkOrderAsync(AddWorkOrderDto addWorkOrderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseWorkOrderDto>> UpdateWorkOrderAsync(UpdateWorkOrderDto updateWorkOrderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseWorkOrderDto>> DeleteWorkOrderAsync(int workOrderId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteWorkOrderResultDto>>> DeleteWorkOrderCollectionAsync(IEnumerable<int> deleteWorkOrderIds, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseWorkOrderDto>>> GetAllResponseWorkOrderDtosAsync(WorkOrderPaginationParams workOrderPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseWorkOrderDto>> GetResponseWorkOrderDtoAsync(GetWorkOrderDto getWorkOrderDto, CancellationToken cancellationToken);
        ValueTask<WorkOrder> GetWorkOrderByIdAsync(int workOrderId, CancellationToken cancellationToken, bool IncludeAllChildren = false);
        ValueTask<ResponseDto<IEnumerable<AssemblyProgressLevelDto>>> CalculateProgressLevelAsync(int workOrderId, CancellationToken cancellationToken, Expression<Func<WorkOrder, bool>> where = null);
        ValueTask<bool> WorkOrderExistsAsync(int workOrderId, CancellationToken cancellationToken);
        ValueTask<bool> WorkOrderExistsAsync(int workOrderId, bool isDeleted, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseWorkOrderAndChildrenDto>> SetStartDateAsync(UpdateWorkOrderSetStartDateDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseWorkOrderAndChildrenDto>> ChangeConditionAsync(UpdateWorkOrderChangeConditionDto dto, CancellationToken cancellationToken);
    }
}
