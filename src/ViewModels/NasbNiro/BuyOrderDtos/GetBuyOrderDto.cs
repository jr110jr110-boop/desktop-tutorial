using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Newtonsoft.Json;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class GetBuyOrderDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int? WorkOrderId { get; set; }

        public static implicit operator GetBuyOrderDto(BuyOrder buyOrder)
        {
            return new GetBuyOrderDto()
            {
                Id = buyOrder.Id,
                WorkOrderId = buyOrder.WorkOrderId
            };
        }

        public static implicit operator GetBuyOrderDto(UpdateBuyOrderDto updateBuyOrderDto)
        {
            return new GetBuyOrderDto()
            {
                Id = updateBuyOrderDto.Id,
                WorkOrderId = updateBuyOrderDto.WorkOrderId
            };
        }

        public static implicit operator GetBuyOrderDto(int buyOrderId)
        {
            return new GetBuyOrderDto()
            {
                Id = buyOrderId,
            };
        }
    }
}
