using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.BomDtos
{
    public sealed class GetBomDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int AssemblyId { get; set; }

        public static implicit operator GetBomDto(Bom bom)
            => new GetBomDto() { Id = bom.Id, AssemblyId = bom.AssemblyId };
    }
}
