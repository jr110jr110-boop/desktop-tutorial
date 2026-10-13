using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro
{
    public class GetEntityDto<KEY>
    {
        [Required]
        public KEY Id { get; set; }
        public virtual bool IncludeTypes { get; set; }
    }
}
