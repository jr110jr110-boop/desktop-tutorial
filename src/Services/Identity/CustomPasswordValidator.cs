using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Services.Contracts.Identity;
using Services.Identity.AccountServices;
using ViewModels.Identity.Settings;

namespace Services.Identity
{
    /// <summary>
    /// Extending the Built-in Password Validation
    /// More info: http://www.dotnettips.info/post/2579
    /// </summary>
    public class CustomPasswordValidator : PasswordValidator<User>
    {
        private readonly IUsedPasswordsService _usedPasswordsService;
        private readonly ISet<string> _passwordsBanList;

        public CustomPasswordValidator(
            IdentityErrorDescriber errors,// How to use CustomIdentityErrorDescriber
            IOptionsSnapshot<SiteSettings> configurationRoot,
            IUsedPasswordsService usedPasswordsService) : base(errors)
        {
            _usedPasswordsService = usedPasswordsService;
            _usedPasswordsService.ThrowIfNull(nameof(_usedPasswordsService));

            configurationRoot.ThrowIfNull(nameof(configurationRoot));
            _passwordsBanList = new HashSet<string>(configurationRoot.Value.PasswordsBanList, StringComparer.OrdinalIgnoreCase);

            if (!_passwordsBanList.Any())
            {
                throw new InvalidOperationException("Please fill the passwords ban list in the appsettings.json file.");
            }
        }

        public override async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string password)
        {
            var errors = new List<IdentityError>();

            if (string.IsNullOrWhiteSpace(password))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordIsNotSet",
                    Description = "لطفا کلمه‌ی عبور را تکمیل کنید."
                });
                return IdentityResult.Failed(errors.ToArray());
            }

            if (string.IsNullOrWhiteSpace(user?.UserName))
            {
                errors.Add(new IdentityError
                {
                    Code = "UserNameIsNotSet",
                    Description = "لطفا نام کاربری را تکمیل کنید."
                });
                return IdentityResult.Failed(errors.ToArray());
            }

            // First use the built-in validator
            var result = await base.ValidateAsync(manager, user, password);
            errors = result.Succeeded ? new List<IdentityError>() : result.Errors.ToList();

            // R1a: the company password rule does not depend on the settings file alone: at least 10 characters with a letter and a digit
            if (password.Length < PasswordPolicy.MinimumLength && !errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordTooShort)))
            {
                errors.Add(new IdentityError
                {
                    Code = nameof(IdentityErrorDescriber.PasswordTooShort),
                    Description = "کلمه‌ی عبور باید دست‌کم ۱۰ نویسه باشد."
                });
            }
            if (!PasswordPolicy.HasLetter(password))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresLetter",
                    Description = "کلمه‌ی عبور باید دست‌کم یک حرف داشته باشد."
                });
            }
            if (!PasswordPolicy.HasDigit(password) && !errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordRequiresDigit)))
            {
                errors.Add(new IdentityError
                {
                    Code = nameof(IdentityErrorDescriber.PasswordRequiresDigit),
                    Description = "کلمه‌ی عبور باید دست‌کم یک رقم داشته باشد."
                });
            }

            // Extending the built-in validator
            if (password.ToLower().Contains(user.UserName.ToLower()))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordContainsUserName",
                    Description = "کلمه‌ی عبور نمی‌تواند حاوی قسمتی از نام کاربری باشد."
                });
                return IdentityResult.Failed(errors.ToArray());
            }

            if (!isSafePasword(password))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordIsNotSafe",
                    Description = "کلمه‌ی عبور وارد شده به سادگی قابل حدس زدن است."
                });
                return IdentityResult.Failed(errors.ToArray());
            }

            if (await _usedPasswordsService.IsPreviouslyUsedPasswordAsync(user, password))
            {
                errors.Add(new IdentityError
                {
                    Code = "IsPreviouslyUsedPassword",
                    Description = "لطفا کلمه‌ی عبور دیگری را انتخاب کنید. این کلمه‌ی عبور پیشتر توسط شما استفاده شده‌است و تکراری می‌باشد."
                });
                return IdentityResult.Failed(errors.ToArray());
            }

            return !errors.Any() ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
        }

        private static bool areAllCharsEuqal(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return false;
            data = data.ToLowerInvariant();
            var firstElement = data.ElementAt(0);
            var euqalCharsLen = data.ToCharArray().Count(x => x == firstElement);
            if (euqalCharsLen == data.Length) return true;
            return false;
        }

        private bool isSafePasword(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return false;
            if (data.Length < 5) return false;
            if (_passwordsBanList.Contains(data.ToLowerInvariant())) return false;
            if (areAllCharsEuqal(data)) return false;

            return true;
        }
    }
}