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

    /// <summary>
    /// The data of the answer of edit-user-roles (R1a2, L-9). It was the comma-joined list of roles; that list is now <see cref="Roles"/>, and the answer tells in the same words as
    /// register-user and update-user whether a request is waiting for a second PMO user: <see cref="Pending"/> is true and <see cref="GrantId"/> is the id to approve or reject.
    /// </summary>
    public class ChangeRoleResultDto
    {
        /// <summary>The roles the user holds now, comma-joined (what the data of this call was before).</summary>
        public string Roles { get; set; }

        /// <summary>True when this call left a request that a second PMO user has to approve (a sensitive role, or the removal of the PMO role).</summary>
        public bool Pending { get; set; }

        /// <summary>Id of the first request this call made or found (role-grants/{GrantId}/approve); null when none is waiting.</summary>
        public int? GrantId { get; set; }

        /// <summary>Ids of every request of this user that is waiting (a user can wait for several roles at once).</summary>
        public int[] GrantIds { get; set; }
    }
}
