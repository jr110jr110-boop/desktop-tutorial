using System.ComponentModel.DataAnnotations;

namespace ViewModels.Dto
{
    public class Token
    {
        [Required]
        public string RefreshToken { get; set; }
    }
}