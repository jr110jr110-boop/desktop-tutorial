using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Entities.AuditableEntity;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Microsoft.AspNetCore.Identity;

namespace Entities.Identity
{
    public class User : IdentityUser<int>, IAuditableEntity
    {
        public User()
        {
            UserUsedPasswords = new HashSet<UserUsedPassword>();
            UserTokens = new HashSet<UserToken>();
            CustomUserTokens = new HashSet<CustomUserToken>();
            //UserProfile = new HashSet<UserProfile>();            
        }

        [StringLength(450)]
        public string Name { get; set; }

        public string LastName { get; set; }


        [NotMapped]
        public string DisplayName
        {
            get
            {
                var displayName = $"{Name} {LastName}";
                return string.IsNullOrWhiteSpace(displayName) ? UserName : displayName;
            }
        }

        public void LogicallyDelete()
        {
            this.IsDeleted = true;
        }

        [StringLength(450)]
        public string PhotoFileName { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset? LastLoggedIn { get; set; }
        public string SerialNumber { get; set; }
        public string MobileConfirmCode { get; set; }
        public string NationalCode { get; set; }
        public DateTimeOffset BirthDate { get; set; }
        public DateTimeOffset? ConfirmCodeSendDate { get; set; }
        public string FcmToken { get; set; }
        public bool IsDeleted { get; set; }
        public virtual ICollection<UserUsedPassword> UserUsedPasswords { get; set; }
        public virtual ICollection<UserToken> UserTokens { get; set; }

        public virtual ICollection<UserRole> Roles { get; set; }
        public virtual ICollection<CustomUserToken> CustomUserTokens { get; set; }
        public virtual ICollection<UserLogin> Logins { get; set; }
        public virtual ICollection<UserClaim> Claims { get; set; }
        public virtual ICollection<ProcessEntity> Processes { get; set; }
        public virtual UserProfile UserProfile { get; set; }
        public Guid? ProfilePhotoId { get; set; }
        public Document ProfilePhoto { get; set; }

    }
}