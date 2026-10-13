using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.AuditableEntity;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace ViewModels.Dto
{
    public class UserDto : IdentityUser<int>, IAuditableEntity
    {
        [StringLength(450)]
        public string Name { get; set; }

        public string LastName { get; set; }

        public string DisplayName { get; set; }


        [StringLength(450)]
        public string PhotoFileName { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset? LastLoggedIn { get; set; }
        public string SerialNumber { get; set; }
        public string MobileConfirmCode { get; set; }
        public DateTimeOffset? ConfirmCodeSendDate { get; set; }
        public string FcmToken { get; set; }

        public bool IsDeleted { get; set; }

        // public virtual ICollection<UserUsedPassword> UserUsedPasswords { get; set; }
        //public virtual ICollection<UserToken> UserTokens { get; set; }

        public virtual ICollection<UserRole> Roles { get; set; }
        //public virtual ICollection<CustomUserToken> CustomUserTokens { get; set; }
        //public virtual ICollection<UserLogin> Logins { get; set; }

        // public virtual ICollection<UserClaim> Claims { get; set; }


        public virtual UserProfile UserProfile { get; set; }


    }

    public class SimpleUserDto
    {
        public int Id { get; set; }

        [StringLength(450)]
        public string Name { get; set; }

        public string LastName { get; set; }

        public string UserName { get; set; }

        public string DisplayName { get; set; }

        public string RegisterDate { get; set; }

        public bool lockoutEnabled { get; set; }

        public string Email { get; set; }

        [StringLength(450)]
        public string PhotoFileName { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset? LastLoggedIn { get; set; }
        public string SerialNumber { get; set; }
        public string MobileConfirmCode { get; set; }
        public DateTimeOffset? ConfirmCodeSendDate { get; set; }

        public bool TwoFactorEnabled { get; set; }

        public bool PhoneNumberConfirmed { get; set; }

        public bool EmailConfirmed { get; set; }

        public string PhoneNumber { get; set; }

        public int AccessFailedCount { get; set; }

        public bool IsDeleted { get; set; }

        public List<UserRoleVM> Roles { get; set; }
    }

    public class UserResetPasswordDto
    {
        public int UserId { get; set; }

        public string Token { get; set; }
        public string NewPassword { get; set; }
    }

    public class UpdateUserFullNameDto
    {
        public UpdateUserFullNameDto(int userId, string name, string lastName)
        {
            UserId = userId;
            Name = name;
            LastName = lastName;
        }
        public int UserId { get; set; }
        public string Name { get; set; }
        public string LastName { get; set; }
    }

    public class UserUpdateDto
    {
        public int UserId { get; set; }
        public string PhotoFileName { get; set; }
        public string SerialNumber { get; set; }

        public string UserName { get; set; }

        public string Email { get; set; }
        public string PhoneNumber { get; set; }

        public bool IsActive { get; set; }

        public string Name { get; set; }
        public string LastName { get; set; }

        public bool IsDeleted { get; set; }
    }
    public class UserListDto
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string RegisterDate { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; } = true;
        public string PhoneNumber { get; set; }
        public int AccessFailedCount { get; set; }
        public string Name { get; set; }
        public string LastName { get; set; }
        public string FatherName { get; set; }
        public string GeoAddress { get; set; }
        public string BirthDate { get; set; }
        public string NationalCode { get; set; }
        public string Gender { get; set; }

        public string Type { get; set; }
        public string Status { get; set; }
        public string Agent { get; set; }

        public string SeriShChar { get; set; }

        public string SeriSh { get; set; }
        public string Serial { get; set; }

        public string ShNumber { get; set; }

        public string PlaceOfIssue { get; set; }
        public string PlaceOfBirth { get; set; }

        public string SignatureFile { get; set; }

        public bool CreatedByAdmin { get; set; }

        public string InviteCode { get; set; }

        public int? InviterUserId { get; set; }

        public bool IsDeleted { get; set; }

        public string BranchName { get; set; }
        public long UserScoreSum { get; set; }
    }
    public class SimpleUserListDto
    {

        public List<UserListDto> List { get; set; }

        public int Count { get; set; }
    }

    public class ChangeUserRoleDto
    {
        public int UserId { get; set; }
        public int[] RoleIds { get; set; }
    }

    public class UserRoleVM
    {
        public UserRoleVM(int userId, int roleId, string roleDescription, string roleName)
        {
            UserId = userId;
            RoleId = roleId;
            RoleDescription = roleDescription;
            RoleName = roleName;
        }
        public int UserId { get; set; }
        public int RoleId { get; set; }

        public string RoleDescription { get; set; }

        public string RoleName { get; set; }
    }
}
