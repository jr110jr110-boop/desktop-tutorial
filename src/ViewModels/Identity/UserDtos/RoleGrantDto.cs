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

        /// <summary>
        /// True when the signed-in PMO user may approve it: not the one who asked, not the user who receives the role, not a PMO whose own PMO role was
        /// given on the request of the same person, and one who has held the PMO role for the minimum time (Identity:ApproverMinDays).
        /// </summary>
        public bool CanApprove { get; set; }

        /// <summary>R1a2: Persian text of the reason why the signed-in user cannot approve it; empty when he or she can.</summary>
        public string CannotApproveReason { get; set; }

        /// <summary>R1a2: "role" (a sensitive role is requested) or "reset" (the password reset of a PMO or sensitive-role holder; Role is then PASSWORD_RESET).</summary>
        public string Kind { get; set; } = "role";

        /// <summary>R1a2: filled only in the answer that approves a "reset" request: the temporary password, shown this once; hand it to the user.</summary>
        public string TemporaryPassword { get; set; }
    }
}
