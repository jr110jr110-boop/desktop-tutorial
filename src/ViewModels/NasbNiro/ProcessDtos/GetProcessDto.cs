using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class GetProcessDto
    {
        [Required]
        public int BomId { get; set; }
        [Required]
        public int Id { get; set; }


        public static implicit operator GetProcessDto(ProcessEntity processEntity)
        {
            return new GetProcessDto()
            {
                Id = processEntity.Id,
                BomId = processEntity.BomId
            };
        }
    }
}
