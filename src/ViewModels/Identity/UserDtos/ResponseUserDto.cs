using System;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.Identity.UserDtos
{
    public class ResponseUserDto
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public string PhoneNumber { get; set; }
        public bool PhoneNumberConfirmed { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public bool LockoutEnabled { get; set; }
        public int AccessFailedCount { get; set; }
        public string Name { get; set; }
        public string LastName { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset? LastLoggedIn { get; set; }
        public string SerialNumber { get; set; }
        public string MobileConfirmCode { get; set; }
        public string NationalCode { get; set; }
        public DateTimeOffset BirthDate { get; set; }
        public DateTimeOffset? ConfirmCodeSendDate { get; set; }
        public bool IsDeleted { get; set; }
        public ResponseUserRoleDto[] Roles { get; set; }

        public Guid? ProfilePhotoId { get; set; }
        public ResponseFileDto ProfilePhoto { get; set; }

        /// <summary>
        /// R1a2 (L-9): filled only in the answers of register-user and update-user (false and null everywhere else): true when a request is waiting for a second PMO user
        /// (a sensitive role, or the removal of the PMO role). <see cref="GrantId"/> is the first such request (role-grants/{id}/approve), <see cref="GrantIds"/> all of them.
        /// </summary>
        public bool Pending { get; set; }
        public int? GrantId { get; set; }
        public int[] GrantIds { get; set; }
    }
}
