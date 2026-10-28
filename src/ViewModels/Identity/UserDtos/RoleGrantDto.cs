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
        /// R1a2 (M-2): a sensitive role also waits until its receiver has chosen a password of his own (the PMO user who registered him, or reset his password, knows the other one);
        /// that holds for every viewer, and <see cref="CannotApproveReason"/> says so.
        /// </summary>
        public bool CanApprove { get; set; }

        /// <summary>R1a2: Persian text of the reason why the signed-in user cannot approve it; empty when he or she can.</summary>
        public string CannotApproveReason { get; set; }

        /// <summary>
        /// R1a2: "role" (a sensitive role is requested), "reset" (the password reset of a PMO or sensitive-role holder; Role is then PASSWORD_RESET) or, since the review fixes,
        /// "revoke" (the PMO role is to be taken away: Role is PMO; or a PMO user's account is to be deleted: Role is USER_DELETE).
        /// </summary>
        public string Kind { get; set; } = "role";

        /// <summary>R1a2: filled only in the answer that approves a "reset" request: the temporary password, shown this once; hand it to the user.</summary>
        public string TemporaryPassword { get; set; }
    }
}
