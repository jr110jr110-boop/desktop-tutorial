using System;

namespace ViewModels.Identity.UserDtos
{
    /// <summary>
    /// A role that needs the approval of a second PMO user before it is given (FINANCEMANAGER, PAYROLL, CEO, AUDITOR, HR, SECRETARIAT).
    /// The request is kept as a claim of the user, so it needs no table.
    /// </summary>
    public class RoleGrantDto
    {
        /// <summary>Id of the request (use it in role-grants/{id}/approve and role-grants/{id}/reject).</summary>
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public int RequestedByUserId { get; set; }
        public string RequestedByName { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }

        /// <summary>True when the signed-in PMO user may approve it: not the one who asked and not the user who receives the role.</summary>
        public bool CanApprove { get; set; }
    }
}
