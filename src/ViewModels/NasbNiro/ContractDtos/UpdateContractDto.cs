using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ContractDtos
{
    public class UpdateContractDto : ContractDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
