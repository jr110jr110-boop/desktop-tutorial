using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.AssemblyDtos
{
    public class UpdateAssemblyChangeStatusDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public AssemblyStatus Status { get; set; }
    }
}
