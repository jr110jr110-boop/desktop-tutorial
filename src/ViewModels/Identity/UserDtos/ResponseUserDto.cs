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
    }
}
