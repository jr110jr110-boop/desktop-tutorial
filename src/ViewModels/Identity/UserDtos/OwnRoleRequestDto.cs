using System;

namespace ViewModels.Identity.UserDtos
{
    /// <summary>FX2: one sensitive role that was asked for the signed-in user and still waits for the second person (own requests only).</summary>
    public class OwnRoleRequestDto
    {
        /// <summary>Role name as in the token (upper case), for example PMO.</summary>
        public string Role { get; set; }

        /// <summary>Persian label of the role for the warning bar.</summary>
        public string RoleLabel { get; set; }

        public DateTimeOffset RequestedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }

        /// <summary>The whole warning in Persian (all pending roles of the user in one sentence); the same on every row.</summary>
        public string WarningText { get; set; }
    }
}
