using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity.UserDtos
{
    /// <summary>
    /// A user changes his or her own password. The current password is always needed; nobody can change another user's password
    /// with this call (a PMO user uses admin-reset-password, which sets a temporary password that must be changed at the next sign-in).
    /// </summary>
    public class ChangePasswordDto
    {
        /// <summary>Optional, kept for older clients: when it is sent it must be the signed-in user's own id.</summary>
        public int? UserId { get; set; }

        /// <summary>The password the user signs in with now (checked here; a wrong value counts toward the sign-in lock-out).</summary>
        public string CurrentPassword { get; set; }

        [Required]
        [MinLength(10)]
        public string NewPassword { get; set; }
    }
}
