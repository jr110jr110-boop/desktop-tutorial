using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.AssemblyDtos
{
    public sealed class GetAssemblyDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int WorkOrderId { get; set; }

        public static implicit operator GetAssemblyDto(AssemblyEntity assembly)
            => new GetAssemblyDto() { Id = assembly.Id, WorkOrderId = assembly.WorkOrderId };
    }
}
