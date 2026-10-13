using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity.UserDtos
{
    public class ChangeRoleDto
    {
        [Required]
        public int UserId { get; set; }

        [Required]
        public int[] RoleIds { get; set; }
    }
}
