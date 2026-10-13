using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProviderDtos
{
    public class ProviderDtoBase
    {
        [Required]
        public string Name { get; set; }
        public string ForContract { get; set; }
        public string Services { get; set; }
        public string Description { get; set; }

        public string CategoryName { get; set; }
        public int CategoryId { get; set; }
        public decimal EvaluationScore { get; set; }
    }
}
