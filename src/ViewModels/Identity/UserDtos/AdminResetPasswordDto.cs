using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity.UserDtos
{
    /// <summary>A PMO user sets a temporary password for another user; the user must change it at the next sign-in.</summary>
    public class AdminResetPasswordDto
    {
        [Required]
        public int UserId { get; set; }

        /// <summary>Optional. When empty the system makes a random one and returns it once in the answer.</summary>
        public string TemporaryPassword { get; set; }
    }

    public class AdminResetPasswordResultDto
    {
        public int UserId { get; set; }
        public string UserName { get; set; }

        /// <summary>Only filled when the system made the password; hand it to the user, it is not shown again.</summary>
        public string TemporaryPassword { get; set; }

        public bool MustChangePassword { get; set; }

        /// <summary>
        /// R1a2: true when nothing was reset yet: the user is a PMO user or holds a sensitive role, so a second PMO user must approve the request
        /// (role-grants/{GrantId}/approve). The temporary password is then made at the approval and shown to the approver.
        /// </summary>
        public bool Pending { get; set; }

        /// <summary>R1a2: id of the pending request when <see cref="Pending"/> is true.</summary>
        public int? GrantId { get; set; }
    }

    /// <summary>The user who signed in with a temporary password sets a new one (anonymous call: the temporary password is the proof).</summary>
    public class ChangeTemporaryPasswordDto
    {
        [Required]
        public string UserName { get; set; }

        [Required]
        public string TemporaryPassword { get; set; }

        [Required]
        [MinLength(10)]
        public string NewPassword { get; set; }
    }
}
