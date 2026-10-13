using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using static Entities.Model.NasbNiro.Contracts.BuyOrder;

namespace Services.NasbNiro
{
    public interface IBuyOrderOutgoingService
    {
        ValueTask<ResponseDto<int>> ApplyFinalReceiptAsync(ApplyFinalReceiptDto dto);
        ValueTask<ResponseDto<int>> ApplyTempReceiptAsync(ApplyTempReceiptDto dto);
    }

    public class BuyOrderOutgoingService : IBuyOrderOutgoingService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly DbSet<BuyOrder> _buyOrders;

        public BuyOrderOutgoingService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _buyOrders = unitOfWork.Set<BuyOrder>().ThrowIfNull(nameof(_buyOrders));
        }

        public async ValueTask<ResponseDto<int>> ApplyTempReceiptAsync(ApplyTempReceiptDto dto)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<int>("داده نامعتبر");
            }

            BuyOrder[] buyOrders = await _buyOrders
                .Where(x => !x.Deleted &&
                            x.Count == dto.Count &&
                            x.EnterCommodityCode == dto.EnterCommodityCode &&
                            BuyOrderHelper.StatusesForAddingTempReceipt.Contains(x.Status))
                .ToArrayAsync();

            if (buyOrders.Length == 0)
            {
                return ResponseDtoHelper.Ok(0, "دستور خریدی برای صدور رسید موقت یافت نشد.");
            }

            Array.ForEach(buyOrders, (item) =>
            {
                item.AddTempReciept(item.LogShitId);
            });

            _buyOrders.UpdateRange(buyOrders);
            if (await _unitOfWork.SaveChangesAsync() > 0)
            {
                return ResponseDtoHelper.Ok(buyOrders.Length, $"عملیات موفقیت آمیز،برای {buyOrders.Length} عدد دستور خرید،رسید موقت صادر شد.");
            }

            return ResponseDtoHelper.BadRequest<int>("رسید موقتی صادر نشد.");
        }

        public async ValueTask<ResponseDto<int>> ApplyFinalReceiptAsync(ApplyFinalReceiptDto dto)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<int>("داده نامعتبر");
            }

            BuyOrder[] buyOrders = await _buyOrders
                .Where(x => !x.Deleted &&
                            x.Count == dto.Count &&
                            x.EnterCommodityCode == dto.EnterCommodityCode &&
                            BuyOrderHelper.StatusesForAddingFinalReceipt.Contains(x.Status))
                .ToArrayAsync();

            if (buyOrders.Length == 0)
            {
                return ResponseDtoHelper.Ok(0, "دستور خریدی برای صدور رسید قطعی یافت نشد.");
            }

            Array.ForEach(buyOrders, (item) =>
            {
                item.WarehouseFinallyAccepted();
            });

            _buyOrders.UpdateRange(buyOrders);
            if (await _unitOfWork.SaveChangesAsync() > 0)
            {
                return ResponseDtoHelper.Ok(buyOrders.Length, $"عملیات موفقیت آمیز،برای {buyOrders.Length} عدد دستور خرید،رسید قطعی صادر شد.");
            }

            return ResponseDtoHelper.BadRequest<int>("رسید قطعی صادر نشد.");
        }


    }

    public class ApplyFinalReceiptDto
    {
        [Required]
        public string EnterCommodityCode { get; set; }

        [Required]
        public int Count { get; set; }
    }

    public class ApplyTempReceiptDto
    {
        [Required]
        public string EnterCommodityCode { get; set; }

        [Required]
        public int Count { get; set; }
    }
}
