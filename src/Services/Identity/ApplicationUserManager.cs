using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Common.IdentityToolkit;
using DataLayer.Context;
using DNTPersianUtils.Core;
using Entities.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.Contracts.Identity;
using ViewModels;
using ViewModels.Dto;
using ViewModels.Exceptions;
using ViewModels.Identity;

namespace Services.Identity
{
    /// <summary>
    /// More info: http://www.dotnettips.info/post/2578
    /// </summary>
    public class ApplicationUserManager :
        UserManager<User>,
        IApplicationUserManager
    {
        private readonly IApplicationRoleManager _roleManager;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IUnitOfWork _uow;
        private readonly IUsedPasswordsService _usedPasswordsService;
        private readonly IdentityErrorDescriber _errors;
        private readonly ILookupNormalizer _keyNormalizer;
        private readonly ILogger<ApplicationUserManager> _logger;
        private readonly IOptions<IdentityOptions> _optionsAccessor;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IEnumerable<IPasswordValidator<User>> _passwordValidators;
        private readonly IServiceProvider _services;
        private readonly DbSet<User> _users;
        private readonly DbSet<Role> _roles;
        private readonly IApplicationUserStore _userStore;
        private readonly IEnumerable<IUserValidator<User>> _userValidators;
        private readonly ISmsSender _smsSender;
        private User _currentUserInScope;
        private readonly AppSettings _appSettings;

        public ApplicationUserManager(
            IApplicationRoleManager roleManager,
            IApplicationUserStore store,
            IOptions<AppSettings> appSettings,
            IOptions<IdentityOptions> optionsAccessor,
            IPasswordHasher<User> passwordHasher,
            IEnumerable<IUserValidator<User>> userValidators,
            IEnumerable<IPasswordValidator<User>> passwordValidators,
            ILookupNormalizer keyNormalizer,
            IdentityErrorDescriber errors,
            IServiceProvider services,
            ILogger<ApplicationUserManager> logger,
            IHttpContextAccessor contextAccessor,
            IUnitOfWork uow,
            ISmsSender smsSender,
            IUsedPasswordsService usedPasswordsService)
            : base((UserStore<User, Role, ApplicationDbContext, int, UserClaim, UserRole, UserLogin, UserToken, RoleClaim>)store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
        {
            _roleManager = roleManager;
            _roleManager.ThrowIfNull(nameof(_roleManager));

            _userStore = store;
            _userStore.ThrowIfNull(nameof(_userStore));
            _appSettings = appSettings.Value;
            _optionsAccessor = optionsAccessor;
            _optionsAccessor.ThrowIfNull(nameof(_optionsAccessor));

            _passwordHasher = passwordHasher;
            _passwordHasher.ThrowIfNull(nameof(_passwordHasher));

            _userValidators = userValidators;
            _userValidators.ThrowIfNull(nameof(_userValidators));

            _passwordValidators = passwordValidators;
            _passwordValidators.ThrowIfNull(nameof(_passwordValidators));

            _keyNormalizer = keyNormalizer;
            _keyNormalizer.ThrowIfNull(nameof(_keyNormalizer));

            _errors = errors;
            _errors.ThrowIfNull(nameof(_errors));

            _services = services;
            _services.ThrowIfNull(nameof(_services));

            _logger = logger;
            _logger.ThrowIfNull(nameof(_logger));

            _contextAccessor = contextAccessor;
            _contextAccessor.ThrowIfNull(nameof(_contextAccessor));

            _uow = uow;
            _uow.ThrowIfNull(nameof(_uow));

            _usedPasswordsService = usedPasswordsService;
            _usedPasswordsService.ThrowIfNull(nameof(_usedPasswordsService));

            _users = uow.Set<User>();
            _roles = uow.Set<Role>();

            _smsSender = smsSender;
            _smsSender.ThrowIfNull(nameof(_smsSender));
        }

        #region BaseClass

        string IApplicationUserManager.CreateTwoFactorRecoveryCode()
        {
            return base.CreateTwoFactorRecoveryCode();
        }

        Task<PasswordVerificationResult> IApplicationUserManager.VerifyPasswordAsync(IUserPasswordStore<User> store, User user, string password)
        {
            return base.VerifyPasswordAsync(store, user, password);
        }

        public override async Task<IdentityResult> CreateAsync(User user)
        {
            var result = await base.CreateAsync(user);
            if (result.Succeeded)
            {
                await _usedPasswordsService.AddToUsedPasswordsListAsync(user);
            }
            return result;
        }

        public override async Task<IdentityResult> CreateAsync(User user, string password)
        {
            var result = await base.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await _usedPasswordsService.AddToUsedPasswordsListAsync(user);
            }
            return result;
        }

        public override async Task<IdentityResult> ChangePasswordAsync(User user, string currentPassword, string newPassword)
        {
            var result = await base.ChangePasswordAsync(user, currentPassword, newPassword);
            if (result.Succeeded)
            {
                await _usedPasswordsService.AddToUsedPasswordsListAsync(user);
            }
            return result;
        }

        public override async Task<IdentityResult> ResetPasswordAsync(User user, string token, string newPassword)
        {
            var result = await base.ResetPasswordAsync(user, token, newPassword);
            if (result.Succeeded)
            {
                await _usedPasswordsService.AddToUsedPasswordsListAsync(user);
            }
            return result;
        }

        #endregion

        #region CustomMethods
        public async Task<string> GetSerialNumberAsync(int userId)
        {
            var user = await FindByIdAsync(userId.ToString());
            return user.SerialNumber;
        }
        public async Task UpdateUserLastActivityDateAsync(int userId)
        {
            var user = FindById(userId);
            if (user.LastLoggedIn != null)
            {
                var updateLastActivityDate = TimeSpan.FromMinutes(2);
                var currentUtc = DateTimeOffset.UtcNow;
                var timeElapsed = currentUtc.Subtract(user.LastLoggedIn.Value);
                if (timeElapsed < updateLastActivityDate)
                {
                    return;
                }
            }
            user.LastLoggedIn = DateTimeOffset.UtcNow;
            await _uow.SaveChangesAsync();
        }
        public User FindById(int userId)
        {
            return _users.Include(c => c.UserUsedPasswords).FirstOrDefault(c => c.Id == userId);
        }

        public int FindByUserName(string userName)
        {
            var user = _users.FirstOrDefault(x => x.UserName == userName);
            if (user != null)
            {
                return user.Id;
            }
            else
            {
                return 0;
            }
        }

        public Task<User> FindByIdIncludeUserRolesAsync(int userId)
        {
            return _users.Include(x => x.Roles).FirstOrDefaultAsync(x => x.Id == userId);
        }

        public Task<List<User>> GetAllUsersAsync()
        {
            return Users.ToListAsync();
        }
        public IEnumerable<User> GetAllUsers()
        {
            return Users.ToList();
        }
        public int GetUserCount()
        {
            return Users.Count();
        }
        public User GetCurrentUser()
        {
            if (_currentUserInScope != null)
            {
                return _currentUserInScope;
            }

            var currentUserId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return null;
            }

            var userId = int.Parse(currentUserId);
            return _currentUserInScope = FindById(userId);
        }

        public async Task<User> GetCurrentUserAsync()
        {
            return _currentUserInScope ??
                (_currentUserInScope = await GetUserAsync(_contextAccessor.HttpContext.User));
        }

        public string GetCurrentUserId()
        {
            return _contextAccessor.HttpContext.User.Identity.GetUserId();
        }

        public int? CurrentUserId
        {
            get
            {
                var userId = _contextAccessor.HttpContext.User.Identity.GetUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return null;
                }

                int result;
                return !int.TryParse(userId, out result) ? (int?)null : result;
            }
        }

        IPasswordHasher<User> IApplicationUserManager.PasswordHasher { get => base.PasswordHasher; set => base.PasswordHasher = value; }

        IList<IUserValidator<User>> IApplicationUserManager.UserValidators => base.UserValidators;

        IList<IPasswordValidator<User>> IApplicationUserManager.PasswordValidators => base.PasswordValidators;

        IQueryable<User> IApplicationUserManager.Users => base.Users;

        public string GetCurrentUserName()
        {
            return _contextAccessor.HttpContext.User.Identity.GetUserName();
        }

        public async Task<bool> HasPasswordAsync(int userId)
        {
            var user = await FindByIdAsync(userId.ToString());
            return user?.PasswordHash != null;
        }

        public async Task<bool> HasPhoneNumberAsync(int userId)
        {
            var user = await FindByIdAsync(userId.ToString());
            return user?.PhoneNumber != null;
        }

        public async Task<PagedUsersListViewModel> GetPagedUsersListAsync(SearchUsersViewModel model, int pageNumber)
        {
            var skipRecords = pageNumber * model.MaxNumberOfRows;
            var query = _users.Include(x => x.Roles).AsNoTracking();

            if (!model.ShowAllUsers)
            {
                query = query.Where(x => x.IsActive == model.UserIsActive);
            }

            if (!string.IsNullOrWhiteSpace(model.TextToFind))
            {
                model.TextToFind = model.TextToFind.ApplyCorrectYeKe();

                if (model.IsPartOfEmail)
                {
                    query = query.Where(x => x.Email.Contains(model.TextToFind));
                }

                if (model.IsUserId)
                {
                    int userId;
                    if (int.TryParse(model.TextToFind, out userId))
                    {
                        query = query.Where(x => x.Id == userId);
                    }
                }

                if (model.IsPartOfName)
                {
                    query = query.Where(x => x.Name.Contains(model.TextToFind));
                }


                if (model.IsPartOfUserName)
                {
                    query = query.Where(x => x.UserName.Contains(model.TextToFind));
                }

            }

            if (model.HasEmailConfirmed)
            {
                query = query.Where(x => x.EmailConfirmed);
            }

            if (model.UserIsLockedOut)
            {
                query = query.Where(x => x.LockoutEnd != null);
            }

            if (model.HasTwoFactorEnabled)
            {
                query = query.Where(x => x.TwoFactorEnabled);
            }

            query = query.OrderBy(x => x.Id);
            return new PagedUsersListViewModel
            {
                Paging =
                {
                    TotalItems = await query.CountAsync()
                },
                Users = await query.Skip(skipRecords).Take(model.MaxNumberOfRows).ToListAsync(),
                Roles = await _roles.ToListAsync()
            };
        }

        public async Task<PagedUsersListViewModel> GetPagedUsersListAsync(
            int pageNumber, int recordsPerPage,
            string sortByField, SortOrder sortOrder,
            bool showAllUsers)
        {
            var skipRecords = pageNumber * recordsPerPage;
            var query = _users.Include(x => x.Roles).AsNoTracking();

            if (!showAllUsers)
            {
                query = query.Where(x => x.IsActive);
            }

            switch (sortByField)
            {
                case nameof(User.Id):
                    query = sortOrder == SortOrder.Descending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id);
                    break;
                default:
                    query = sortOrder == SortOrder.Descending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id);
                    break;
            }

            return new PagedUsersListViewModel
            {
                Paging =
                {
                    TotalItems = await query.CountAsync()
                },
                Users = await query.Skip(skipRecords).Take(recordsPerPage).ToListAsync(),
                Roles = await _roles.ToListAsync()
            };
        }

        public async Task<IdentityResult> UpdateUserAndSecurityStampAsync(int userId, Action<User> action)
        {
            var user = await FindByIdIncludeUserRolesAsync(userId);
            if (user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            action(user);

            var result = await UpdateAsync(user);
            if (!result.Succeeded)
            {
                return result;
            }
            return await UpdateSecurityStampAsync(user);
        }

        public async Task<IdentityResult> AddOrUpdateUserRolesAsync(int userId, IList<int> selectedRoleIds, Action<User> action = null)
        {
            var user = await FindByIdIncludeUserRolesAsync(userId);
            if (user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            var currentUserRoleIds = user.Roles.Select(x => x.RoleId).ToList();

            if (selectedRoleIds == null)
            {
                selectedRoleIds = new List<int>();
            }
            var newRolesToAdd = selectedRoleIds.Except(currentUserRoleIds).ToList();
            foreach (var roleId in newRolesToAdd)
            {
                user.Roles.Add(new UserRole { RoleId = roleId, UserId = user.Id });
            }

            var removedRoles = currentUserRoleIds.Except(selectedRoleIds).ToList();
            foreach (var roleId in removedRoles)
            {
                var userRole = user.Roles.SingleOrDefault(ur => ur.RoleId == roleId);
                if (userRole != null)
                {
                    user.Roles.Remove(userRole);
                }
            }

            action?.Invoke(user);

            var result = await UpdateAsync(user);
            if (!result.Succeeded)
            {
                return result;
            }
            return await UpdateSecurityStampAsync(user);
        }

        public async Task<UsersListViewModel> GetUsersListAsync(DataTableAjaxPostModel dataTable)
        {
            int pageSize = Convert.ToInt32(dataTable.pageSize);
            int skip = Convert.ToInt32(dataTable.pageNumber);
            string sortBy = "Id";
            string sortDir = "desc";

            sortBy = dataTable.column;
            sortDir = dataTable.dir.ToLower();

            var users = _users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(dataTable.search))
            {
                users = users.Where(x => x.DisplayName.Contains(dataTable.search) || x.UserName.Contains(dataTable.search));
            }

            List<UserProfileViewModel> user = await users.Select(x => new UserProfileViewModel
            {
                FirstName = x.DisplayName,
                UserName = x.UserName,
                regDate = x.CreatedDateTime != null ? x.CreatedDateTime.ToPersianDateTextify(DateTimeOffsetPart.IranLocalDateTime) : "",
                id = x.Id,
                IsActive = x.IsActive,
                lockoutEnabled = x.LockoutEnabled,
                PhotoFileName = x.PhotoFileName,
                //LastLoggedIn = x.LastLoggedIn.ToFriendlyPersianDateTextify(),
                Email = x.Email,
                MobileConfirmCode = x.MobileConfirmCode,
            })
           .OrderBy(sortBy + " " + sortDir)
           .Skip(skip)
           .Take(pageSize)
           .ToListAsync();
            return new UsersListViewModel { UserCount = users.Count(), Users = user };
        }

        public async Task<bool> CheckConfirmCodeAsync(RegisterConfirmViewModel confirm)
        {
            var result = await _users.Where(x => x.MobileConfirmCode.ToString() == confirm.ConfirmCode
                                                 && x.NormalizedUserName == confirm.Username
                                                 && x.ConfirmCodeSendDate.Value.AddMinutes(2) > DateTime.Now).AnyAsync();
            if (result)
            {
                var user = await FindByNameAsync(confirm.Username);
                user.PhoneNumberConfirmed = true;
                user.IsActive = true;
                _users.Update(user);
                await _uow.SaveChangesAsync();
                return true;
            }
            return false;
        }

        public async Task ResendConfirmCodeAsync(string userName)
        {
            var user = await FindByNameAsync(userName);
            if (user == null)
            {
                throw new RecordNotFoundException("این شماره موبایل وجود ندارد");
            }
            Random rnd = new Random();
            string mobileConfirmCode = rnd.Next(10000, 99999).ToString();
            user.MobileConfirmCode = mobileConfirmCode;
            user.ConfirmCodeSendDate = DateTime.Now;
            string[] token = new string[7] { user.PhoneNumber, mobileConfirmCode.ToString(), "", "", "", "", "" };
            _smsSender.SendSms(token, "code");//ClubSMS

            _users.Update(user);
            await _uow.SaveChangesAsync();
        }

        public async Task<string> GetConfirmCodeAsync(string userName)
        {
            var user = await FindByNameAsync(userName);
            return user.MobileConfirmCode;
        }


        public async Task<string> GetPhoneNumberAsync(string userName)
        {
            var user = await FindByNameAsync(userName);
            return user.PhoneNumber;
        }

        public async Task<string> GetFullNameAsync(string userName)
        {
            var user = await FindByNameAsync(userName);
            string fullName = user.Name + " " + user.LastName;
            return String.IsNullOrWhiteSpace(fullName) ? userName : fullName;
        }
        public async Task<List<string>> GetAllFcmTokenAsync()
        {
            List<string> result =
                await _users.Where(x => x.FcmToken != null).Select(x => x.FcmToken).ToListAsync();

            return result;
        }

        public async Task UpdateUserTokenFcmAsync(int userId, string token)
        {
            var user = FindById(userId);
            user.FcmToken = token;
            bool result = await _uow.SaveChangesAsync() > 0;
        }


        public async Task<List<string>> GetAllFroumFcmTokenAsync()
        {
            return await _users.Where(x => x.FcmToken != null).Select(x => x.FcmToken).ToListAsync();
        }

        public string GenerateCustomDefaultPassword()
        {
            return "@uSerPass#2021!WO0rD@";
        }

        public async Task<IdentityResult> DeleteUserAccountAsync(int userId)
        {
            IdentityResult rc = new IdentityResult();

            var user = await FindByIdAsync(userId.ToString());
            rc = await DeleteAsync(user);

            return rc;
        }
        public async Task<IdentityResult> DeleteUserAccountAsync(string userName)
        {
            IdentityResult rc = new IdentityResult();

            var user = await FindByNameAsync(userName);
            rc = await DeleteAsync(user);

            return rc;
        }
        public async Task UpdateTokenFcmAsync(int userId, string token)
        {
            var user = FindById(userId);
            user.FcmToken = token;
            await _uow.SaveChangesAsync();
        }

        public async Task<string> GetUserFcmTokenAsync()
        {
            var userId = int.Parse(GetCurrentUserId());
            var user = await _users.FirstOrDefaultAsync(x => x.Id == userId);
            return user.FcmToken;
        }
        public async Task<string> GetUserFcmTokenAsync(int userId)
        {
            var user = await _users.FirstOrDefaultAsync(x => x.Id == userId);
            return user.FcmToken;
        }
        #endregion


        public Task<List<SimpleUserDto>> GetAllUsersWithProfilesAsync()
        {
            return Users.Include(x => x.UserProfile).Include(x => x.Roles).ThenInclude(x => x.Role).Select(user => new SimpleUserDto()
            {
                Id = user.Id,
                Name = user.Name,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                PhotoFileName = user.PhotoFileName,
                CreatedDateTime = user.CreatedDateTime,
                IsActive = user.IsActive,
                LastLoggedIn = user.LastLoggedIn,
                SerialNumber = user.SerialNumber,
                MobileConfirmCode = user.MobileConfirmCode,
                ConfirmCodeSendDate = user.ConfirmCodeSendDate,
                UserName = user.UserName,
                RegisterDate = user.CreatedDateTime != null ? user.CreatedDateTime.ToPersianDateTextify(DateTimeOffsetPart.IranLocalDateTime) : "",
                lockoutEnabled = user.LockoutEnabled,
                Email = user.Email,
                AccessFailedCount = user.AccessFailedCount,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                Roles = user.Roles == null ? null : (
                   user.Roles.Select(role => new UserRoleVM(
                            role.UserId,
                            role.RoleId,
                            role.Role.Description,
                            role.Role.Name
                        )).ToList()
                ),
                IsDeleted = user.IsDeleted
                //UserProfile = user.UserProfile
            }).ToListAsync();

        }

        public async Task<SimpleUserDto> FindByIdAsync(int userId)//SimpleUserDto
        {
            var _user = await _users
            .Where(x => x.Id == userId)
            .Include(x => x.Roles).ThenInclude(x => x.Role)
            .Include(x => x.UserProfile)
            .ThenInclude(x => x.Addresses)
                .ThenInclude(x => x.Country)

            .Include(x => x.UserProfile.Addresses)
                .ThenInclude(x => x.City)

            .Include(x => x.UserProfile.Addresses)
                .ThenInclude(x => x.Province)

            .Include(x => x.UserProfile.Addresses)
                .ThenInclude(x => x.Section)

            .ToListAsync();

            return _user.Select(user => new SimpleUserDto()
            {
                Id = user.Id,
                Name = user.Name,
                LastName = user.LastName,
                UserName = user.UserName,
                DisplayName = user.DisplayName,
                RegisterDate = user.CreatedDateTime != null ? user.CreatedDateTime.ToPersianDateTextify(DateTimeOffsetPart.IranLocalDateTime) : "",
                lockoutEnabled = user.LockoutEnabled,
                Email = user.Email,
                PhotoFileName = user.PhotoFileName,
                CreatedDateTime = user.CreatedDateTime,
                IsActive = user.IsActive,
                LastLoggedIn = user.LastLoggedIn,
                SerialNumber = user.SerialNumber,
                MobileConfirmCode = user.MobileConfirmCode,
                ConfirmCodeSendDate = user.ConfirmCodeSendDate,
                AccessFailedCount = user.AccessFailedCount,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                Roles = user.Roles == null ? null : (
                   user.Roles.Select(role => new UserRoleVM(
                            role.UserId,
                            role.RoleId,
                            role.Role.Description,
                            role.Role.Name
                        )).ToList()
                ),
                IsDeleted = user.IsDeleted
            }).FirstOrDefault();
        }

        public Task<List<UserDto>> GetAllUsersWithFullDataAsync(bool isDeleted)
        {
            return Users.Include("UserProfile").Where(x => x.IsDeleted == isDeleted).Select(user => new UserDto()
            {
                Id = user.Id,
                Name = user.Name,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                PhotoFileName = user.PhotoFileName,
                CreatedDateTime = user.CreatedDateTime,
                IsActive = user.IsActive,
                LastLoggedIn = user.LastLoggedIn,
                SerialNumber = user.SerialNumber,
                MobileConfirmCode = user.MobileConfirmCode,
                ConfirmCodeSendDate = user.ConfirmCodeSendDate,
                FcmToken = user.FcmToken,
                //UserUsedPasswords = user.UserUsedPasswords,
                //UserTokens = user.UserTokens,
                Roles = user.Roles,
                //CustomUserTokens = user.CustomUserTokens,
                //Logins = user.Logins,
                //Claims = user.Claims,
                UserProfile = user.UserProfile,
                IsDeleted = user.IsDeleted
            }).ToListAsync();

        }

        public async Task<IdentityResult> UpdateUserAsync(UserUpdateDto model)
        {
            var _user = await FindByIdIncludeUserRolesAsync(model.UserId);
            if (_user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            _user.PhotoFileName = model.PhotoFileName;
            _user.SerialNumber = model.SerialNumber;
            _user.UserName = model.UserName;
            _user.Email = model.Email;
            _user.PhoneNumber = model.PhoneNumber;
            _user.IsActive = model.IsActive;
            _user.Name = model.Name;
            _user.LastName = model.LastName;
            _user.IsDeleted = model.IsDeleted;

            var result = await UpdateAsync(_user);
            return result;
        }


        public async Task<SimpleUserListDto> GetSimpleUsersListAsync(DataTableAjaxPostModel dataTable)
        {
            int pageSize = Convert.ToInt32(dataTable.pageSize == 0 ? 10 : dataTable.pageSize);
            int skip = Convert.ToInt32(dataTable.pageNumber);
            string sortBy = "Id";
            string sortDir = "desc";

            if (dataTable.column != null)
            {
                sortBy = dataTable.column;
            }

            if (dataTable.dir != null)
            {
                sortDir = dataTable.dir.ToLower();
            }

            var users = _users.Include(x => x.UserProfile).AsQueryable();
            if (!String.IsNullOrWhiteSpace(dataTable.filterBy) && !String.IsNullOrWhiteSpace(dataTable.filterValue))
            {
                if (dataTable.filterBy == "Role")
                {
                    users = users.Where(x => x.Roles.Any(m => m.Role.NormalizedName == dataTable.filterValue));
                }

                if (dataTable.filterBy == "IsDeleted")
                {
                    users = users.Where(x => x.IsDeleted == Convert.ToBoolean(dataTable.filterValue.ToLower()));
                }
            }

            if (!string.IsNullOrWhiteSpace(dataTable.search))
            {
                users = users.Where(x => x.DisplayName.Contains(dataTable.search) || x.UserName.Contains(dataTable.search));
            }

            List<UserListDto> _lstUsers = await users.Select(user => new UserListDto
            {
                Id = user.Id,
                IsActive = user.IsActive,
                UserName = user.UserName,
                RegisterDate = user.CreatedDateTime != null ? user.CreatedDateTime.ToPersianDateTextify(DateTimeOffsetPart.IranLocalDateTime) : "",
                Email = user.Email,
                AccessFailedCount = user.AccessFailedCount,
                PhoneNumber = user.PhoneNumber,
                NationalCode = user.UserProfile != null ? user.UserProfile.NationalCode : "",
                Gender = user.UserProfile != null ? (user.UserProfile.Gender ? "آقا" : "خانم") : "",
                BirthDate = user.UserProfile != null ? (user.UserProfile.BirthDate != null ? (Convert.ToDateTime(user.UserProfile.BirthDate).ToPersianDateTime().ToString().Split(' ')[0]) : "") : "",
                IsDeleted = user.IsDeleted,
                Name = user.Name,
                LastName = user.LastName,
                BranchName = user.UserProfile != null ? user.UserProfile.BranchName : "",
                UserScoreSum = long.MinValue
            })

           .OrderBy(sortBy + " " + sortDir)
           .Skip(skip)
           .Take(pageSize)
           .ToListAsync();
            return new SimpleUserListDto { Count = users.Count(), List = _lstUsers };
        }

        public async Task<IdentityResult> UpdateUserFullNameAsync(UpdateUserFullNameDto model)
        {
            var _user = await FindByIdIncludeUserRolesAsync(model.UserId);
            if (_user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            _user.Name = model.Name;
            _user.LastName = model.LastName;

            var result = await UpdateAsync(_user);
            return result;
        }

        public async Task<IdentityResult> LogicallyDeleteUserAsync(int userId)
        {
            var _user = await FindByIdIncludeUserRolesAsync(userId);
            if (_user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            _user.LogicallyDelete();

            var result = await UpdateAsync(_user);
            return result;
        }

        public async Task<IdentityResult> UpdateUserIsDeletedAsync(int userId, bool isDeleted)
        {
            var _user = await FindByIdIncludeUserRolesAsync(userId);
            if (_user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            _user.IsDeleted = isDeleted;

            var result = await UpdateAsync(_user);
            return result;
        }
        public async Task<string> GetUserNationaCodeByIdAsync(int userId)
        {
            string nationalCode = "";
            var user = await _users.Include(x => x.UserProfile).Where(x => x.Id == userId).FirstOrDefaultAsync();
            if (user != null && user.UserProfile != null)
                nationalCode = user.UserProfile.NationalCode;


            return nationalCode;
        }

        public async Task<User> FindByNameIncludeProfileDataAsync(string userName)
        {
            return await _users.Include(x => x.UserProfile).FirstOrDefaultAsync(x => x.UserName == userName);
        }

        public async Task<IdentityResult> UpdateUserIsActiveAndIsDeletedAsync(int userId, bool isActive, bool isDeleted)
        {
            var _user = await FindByIdIncludeUserRolesAsync(userId);
            if (_user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            _user.IsActive = isActive;
            _user.IsDeleted = isDeleted;

            var result = await UpdateAsync(_user);
            return result;
        }

        public async Task<IdentityResult> UpdateUserIsActiveAsync(int userId, bool isActive)
        {
            var _user = await FindByIdIncludeUserRolesAsync(userId);
            if (_user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            _user.IsActive = isActive;

            var result = await UpdateAsync(_user);
            return result;
        }

        public async Task<IdentityResult> UpdateUserConfirmCodeAsync(int userId, string mobileConfirmCode)
        {
            var _user = await FindByIdIncludeUserRolesAsync(userId);
            if (_user == null)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "کاربر مورد نظر یافت نشد."
                });
            }

            _user.MobileConfirmCode = mobileConfirmCode;
            _user.ConfirmCodeSendDate = DateTime.Now;

            var result = await UpdateAsync(_user);
            return result;
        }

    }
}