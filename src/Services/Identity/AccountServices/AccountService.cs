using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.Extentions;
using Common.GuardToolkit;
using Common.IdentityToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Identity.Constants;
using Entities.Model;
using Entities.Resources;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Contracts.Identity;
using Services.NasbNiro.Contracts;
using Services.Resources;
using Services.Services;
using Services.Token;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro;
using ViewModels.NasbNiro.Pagination;

namespace Services.Identity.AccountServices
{
    public class AccountService : IAccountService, ISaveChangesAsync
    {
        private readonly IApplicationUserManager _userManager;
        private readonly IApplicationRoleManager _roleManager;
        private readonly IApplicationSignInManager _signInManager;
        private readonly ITokenFactoryService _tokenFactoryService;
        private readonly IUnitOfWork _uow;
        private readonly ITokenStoreService _tokenStoreService;
        private readonly ILogger<AccountService> _logger;
        private readonly IMapper _mapper;
        private readonly IDocumentService _documentService;
        private readonly DbSet<UserRole> _userRole;
        public AccountService(
            IApplicationUserManager userManager,
            IApplicationRoleManager roleManager,
            IApplicationSignInManager signInManager,
            ITokenFactoryService tokenFactoryService,
            IUnitOfWork uow,
            ITokenStoreService tokenStoreService,
            ILogger<AccountService> logger,
            IMapper mapper,
            IDocumentService documentService)
        {
            _userManager = userManager.ThrowIfNull(nameof(userManager));
            _roleManager = roleManager.ThrowIfNull(nameof(roleManager));
            _signInManager = signInManager.ThrowIfNull(nameof(signInManager));
            _tokenFactoryService = tokenFactoryService.ThrowIfNull(nameof(tokenFactoryService));
            _uow = uow.ThrowIfNull(nameof(uow));
            _tokenStoreService = tokenStoreService.ThrowIfNull(nameof(tokenStoreService));
            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _documentService = documentService.ThrowIfNull(nameof(documentService));
            _userRole = _uow.Set<UserRole>();
        }

        public async ValueTask<ResponseDto<ResponseUserDto>> RegisterAsync(RegisterUserDto registerUserDto, CancellationToken cancellationToken)
        {
            if (registerUserDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(AccountServiceMessages.NullDataReceived);

            // nobody may register a name that belongs to an AI seat
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(registerUserDto.UserName))
                return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(AccountServiceMessages.InvalidLoginData);

            var user = await _userManager
                .FindByNameAsync(registerUserDto.UserName)
                .WithCancellationTokenAsync(cancellationToken);

            if (user != null && user.IsDeleted == false)
                return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(AccountServiceMessages
                        .UserNameIsUsedBefore);

            IEnumerable<Role> roles = await _roleManager
                .Roles.Where(r => registerUserDto.RoleIds.Any(rid => rid == r.Id)).ToListAsync(cancellationToken);

            user = new User
            {
                UserName = registerUserDto.UserName,
                Name = registerUserDto.Name,
                LastName = registerUserDto.LastName,
                Email = registerUserDto.Email,
                PhoneNumber = registerUserDto.PhoneNumber,
                BirthDate = registerUserDto.BirthDate,
                NationalCode = registerUserDto.NationalCode,
                EmailConfirmed = true,
                LockoutEnabled = false,
                IsActive = true,
                IsDeleted = false
            };

            var createUserResult = await _userManager
                .CreateAsync(user, registerUserDto.Password)
                .WithCancellationTokenAsync(cancellationToken);

            if (!createUserResult.Succeeded)
            {
                _logger.LogError($"{createUserResult.Errors}");

                return ResponseDtoHelper
                   .BadRequest<ResponseUserDto>(AccountServiceMessages
                       .FailedToRegisterUser + createUserResult.DumpErrors());
            }

            var addToRoleResult = await _userManager
                .AddToRolesAsync(user, roles.Select(r => r.Name))
                .WithCancellationTokenAsync(cancellationToken);

            if (addToRoleResult == IdentityResult.Failed())
            {
                _logger.LogError($"{addToRoleResult.Errors}");
                await _userManager.DeleteAsync(user).WithCancellationTokenAsync(cancellationToken);

                return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(AccountServiceMessages
                        .FailedToAssignRole);
            }

            return await GetResponseUserDtoByIdAsync(getUserDto:
                    new GetUserDto()
                    {
                        Id = user.Id,
                    }, cancellationToken: cancellationToken);
        }

        public async ValueTask<ResponseDto<ResponseLoginUserDto>> LoginAsync(LoginUserDto loginUserDto, CancellationToken cancellationToken)
        {
            if (loginUserDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.NullDataReceived);

            // the service users of the AI seats ("seat.*") are locked out for good; this check keeps them out even if a lockout were ever lifted
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(loginUserDto.UserName))
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.InvalidLoginData);

            var user = await _userManager
                .FindByNameAsync(loginUserDto.UserName)
                .WithCancellationTokenAsync(cancellationToken);

            if (user is null)
                return ResponseDtoHelper
                    .NotFound<ResponseLoginUserDto>
                    (AccountServiceMessages.UserNotFound);

            if (!user.IsActive)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.AccountIsNotActive);

            if (user.IsDeleted)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.AccountIsDeleted);

            SignInResult signInResult = await _signInManager.PasswordSignInAsync(
                  loginUserDto.UserName,
                  loginUserDto.Password,
                  false,
                  lockoutOnFailure: false)
                .WithCancellationTokenAsync(cancellationToken);

            if (signInResult.IsLockedOut)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.AccountIsLockedOut);

            if (!signInResult.Succeeded)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.InvalidLoginData);

            JwtTokensData token = await _tokenFactoryService
                .CreateJwtTokensAsync(user)
                .WithCancellationTokenAsync(cancellationToken);

            await _tokenStoreService.AddUserTokenAsync(
                user,
                token.RefreshTokenSerial,
                token.AccessToken,
                null)
                .WithCancellationTokenAsync(cancellationToken);

            await _uow.SaveChangesAsync(cancellationToken);

            user.LastLoggedIn = DateTime.Now;
            await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            IEnumerable<string> userRoles = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);
            IQueryable<RoleClaim> roleClaimsQuery = _uow.Set<RoleClaim>().AsNoTracking();

            roleClaimsQuery = roleClaimsQuery.Where(rc =>
                userRoles.Any(ur => EF.Functions.Like(rc.Role.Name, ur)));

            string[] accessList = await roleClaimsQuery
                .Select(rc => rc.ClaimValue)
                .Distinct()
                .ToArrayAsync(cancellationToken);

            ResponseLoginUserDto responseLoginUserDto
                = new ResponseLoginUserDto()
                {
                    AccessToken = token.AccessToken,
                    RefreshToken = token.RefreshToken,
                    AccessList = accessList,
                };

            return ResponseDtoHelper
                .Ok(responseLoginUserDto, AccountServiceMessages.LogedInSuccessfully);
        }

        public async ValueTask<ResponseDto<ResponseLoginUserDto>> RefreshTokenAsync(RefreshTokenDto refreshTokenDto, CancellationToken cancellationToken)
        {
            if (refreshTokenDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.NullDataReceived);

            string refreshToken = refreshTokenDto.RefreshToken;

            if (string.IsNullOrWhiteSpace(refreshToken))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(
                    AccountServiceMessages.InvalidRefreshToken);

            var storagetoken = await _tokenStoreService.FindTokenAsync(refreshToken);
            if (storagetoken is null)
                return ResponseDtoHelper.Unauthorized<ResponseLoginUserDto>(
                    AccountServiceMessages.InvalidRefreshToken);

            if (storagetoken.User == null || storagetoken.User.IsDeleted == true)
                return ResponseDtoHelper.NotFound<ResponseLoginUserDto>(
                    AccountServiceMessages.UserNotFound);

            var token = await _tokenFactoryService
                .CreateJwtTokensAsync(storagetoken.User)
                .WithCancellationTokenAsync(cancellationToken);

            await _tokenStoreService.AddUserTokenAsync(
                    storagetoken.User,
                    token.RefreshTokenSerial,
                    token.AccessToken,
                    _tokenFactoryService.GetRefreshTokenSerial(refreshToken))
                .WithCancellationTokenAsync(cancellationToken);

            await _uow.SaveChangesAsync(cancellationToken);

            ResponseLoginUserDto responseLoginUserDto
                = new ResponseLoginUserDto()
                {
                    AccessToken = token.AccessToken,
                    RefreshToken = token.RefreshToken
                };

            return ResponseDtoHelper.Ok(responseLoginUserDto);
        }

        public async ValueTask<ResponseDto<bool>> LogoutAsync(string userId, string refreshToken, CancellationToken cancellationToken)
        {
            await _tokenStoreService.RevokeUserBearerTokensAsync(userId, refreshToken).WithCancellationTokenAsync(cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper
                     .Ok(true, AccountServiceMessages.LogedOutSuccessfully);
        }

        public async ValueTask<ResponseDto<ResponseLoginUserDto>> ChangePasswordAsync(ChangePasswordDto changePasswordDto, CancellationToken cancellationToken)
        {

            if (changePasswordDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.NullDataReceived);

            var passwordScore = UtilityFuncs
                .IsPasswordStrength(changePasswordDto.NewPassword);

            if (passwordScore == PasswordScore.Blank ||
                passwordScore == PasswordScore.VeryWeak)
            {
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>
                     (AccountServiceMessages.WeakPasswordDetected);
            }

            var user = await _userManager.Users
                .SingleOrDefaultAsync(u => u.Id == changePasswordDto.UserId, cancellationToken);

            if (user == null || user.IsDeleted == true)
                return ResponseDtoHelper.NotFound<ResponseLoginUserDto>
                         (AccountServiceMessages.UserNotFound);

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(SeatUserMessage);

            string token = await _userManager.GeneratePasswordResetTokenAsync(user)
                .WithCancellationTokenAsync(cancellationToken);

            IdentityResult result = await this._userManager
                .ResetPasswordAsync(user, token, changePasswordDto.NewPassword).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
                return await this.LoginAsync(new LoginUserDto()
                {
                    UserName = user.UserName,
                    Password = changePasswordDto.NewPassword
                },
                cancellationToken);

            return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.ChangePasswordFailed + Environment.NewLine + result.DumpErrors());
        }

        private const string SeatUserMessage = "کاربر سرویس صندلی هوش مصنوعی را فقط خود سامانه مدیریت می‌کند.";

        public async ValueTask<ResponseDto<string>> ChangeRoleAsync(ChangeRoleDto changeRoleDto, CancellationToken cancellationToken)
        {
            if (changeRoleDto is null)
                return ResponseDtoHelper.BadRequest<string>
                    (AccountServiceMessages.NullDataReceived);

            User user = await _userManager
                .FindByIdAsync(changeRoleDto.UserId.ToString())
                .WithCancellationTokenAsync(cancellationToken);

            if (user is null)
                return ResponseDtoHelper.NotFound<string>
                    (AccountServiceMessages.UserNotFound);

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<string>(SeatUserMessage);

            IList<string> selectedRoles = await _roleManager
                .Roles.Where(role => changeRoleDto.RoleIds.Any(ri => ri == role.Id))
                .Select(role => role.NormalizedName)
                .ToListAsync(cancellationToken);

            IList<string> userRoles = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);

            userRoles = userRoles.Select(role => role.ToUpper()).ToList();

            IdentityResult result = await _userManager
                .AddToRolesAsync(user, selectedRoles.Except(userRoles))
                .WithCancellationTokenAsync(cancellationToken);

            if (!result.Succeeded)
                return ResponseDtoHelper
                    .BadRequest<string>
                    (AccountServiceMessages.FailedToAssignRole + result.DumpErrors());

            result = await _userManager.RemoveFromRolesAsync(
                    user,
                    userRoles.Except(selectedRoles))
                .WithCancellationTokenAsync(cancellationToken);

            if (!result.Succeeded)
                return ResponseDtoHelper
                    .BadRequest<string>
                    (AccountServiceMessages.FailedToAssignRole + result.Errors.ToString());

            IList<string> userCurrentRoles = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);

            return ResponseDtoHelper.Ok(
                string.Join(",", userCurrentRoles), AccountServiceMessages.RolesChangedSuccessfully);
        }

        public async ValueTask<ResponseDto<IEnumerable<ResponseRole>>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            List<Role> roles = await _roleManager.GetAllCustomRolesAsync().WithCancellationTokenAsync(cancellationToken);

            return ResponseDtoHelper.Ok(roles.Select(role => new ResponseRole()
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description
            }));
        }

        public async ValueTask<ResponseDto<ResponseUserDto>> GetUserInfoAsync(CancellationToken cancellationToken)
        {
            string currentUserId = _userManager.GetCurrentUserId();
            int userId = int.Parse(currentUserId);

            ResponseDto<ResponseUserDto> result = await GetResponseUserDtoByIdAsync(getUserDto:
                    new GetUserDto()
                    {
                        Id = userId,
                    },
                    cancellationToken: cancellationToken);

            if (result.Data is null)
                return ResponseDtoHelper.NotFound<ResponseUserDto>
                    (AccountServiceMessages.UserNotFound);
            return result;
        }

        public async Task<ResponseDto<ResponseDashboardUserDto>> GetUserDashboardInfoAsync(CancellationToken cancellationToken)
        {
            string currentUserId = _userManager.GetCurrentUserId();
            int userId = int.Parse(currentUserId);

            ResponseDashboardUserDto dashboardUser =
                await _userManager
                .Users
                .Where(x => x.Id == userId)
                .ProjectTo<ResponseDashboardUserDto>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(cancellationToken);

            if (dashboardUser is null)
                return ResponseDtoHelper.NotFound<ResponseDashboardUserDto>
                    (AccountServiceMessages.UserNotFound);

            return ResponseDtoHelper.Ok(dashboardUser);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseUserDto>>> GetAllUsersAsync(
            UserPaginationParams userPaginationParams, CancellationToken cancellationToken)
        {
            // the service users of the AI seats are not in the user list
            IQueryable<User> users = _userManager.Users.Where(u => !u.UserName.StartsWith("seat."));

            if (userPaginationParams.IsDeleted.HasValue)
                users = users.Where(u => u.IsDeleted == userPaginationParams.IsDeleted);

            if (userPaginationParams.Id.HasValue)
                users = users.Where(u => u.Id == userPaginationParams.Id);


            if (!string.IsNullOrEmpty(userPaginationParams.Contains))
            {
                string[] strArray = userPaginationParams.Contains.Split(' ');

                users = users.Where(u =>
                    u.Name != null && strArray.Any(str => u.Name.Contains(str)) ||
                    u.Email != null && strArray.Any(str => u.Email.Contains(str)) ||
                    u.UserName != null && strArray.Any(str => u.UserName.Contains(str)) ||
                    u.PhoneNumber != null && strArray.Any(str => u.PhoneNumber.Contains(str)) ||
                    u.LastName != null && strArray.Any(str => u.LastName.Contains(str)));
            }

            if (!string.IsNullOrEmpty(userPaginationParams.Role))
            {
                users = from user in users
                        where user.Roles.Any(x => EF.Functions.Like(x.Role.Name, $"%{userPaginationParams.Role}%"))
                        select user;
            }

            IQueryable<ResponseUserDto> result;

            result = users.ProjectTo<ResponseUserDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseUserDto>> data = PagedList<ResponseUserDto>
                            .CreateAsync(result,
                                         userPaginationParams.PageNumber,
                                         userPaginationParams.PageSize,
                                         cancellationToken);
            

            return ResponseDtoHelper.Ok(await data);
        }

        public async ValueTask<ResponseDto<ResponseUserShortListDto>> GetEngineerUsersAsync(CancellationToken cancellationToken)
        {
            IQueryable<UserRole> _userRoleQueryable = _userRole.Include(role => role.User).Where(role => role.RoleId == 8);

            List<ResponseUserShortDto> resultQuery = await _userRoleQueryable
               .Select(role => new ResponseUserShortDto()
               {
                   Id = role.User.Id,
                   DisplayName = role.User.DisplayName
               }).ToListAsync();

            return ResponseDtoHelper.Ok(new ResponseUserShortListDto() { Success = true, Message = "", Data = resultQuery });
        }

        public async ValueTask<ResponseDto<ResponseUserDto>> UpdateUserAsync(UpdateUserDto updateUserDto, CancellationToken cancellationToken)
        {
            if (updateUserDto is null)
                return ResponseDtoHelper.BadRequest<ResponseUserDto>(NasbNiroMessages.InvalidData);

            User user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == updateUserDto.Id, cancellationToken);

            if (user is null)
                return ResponseDtoHelper
                    .NotFound<ResponseUserDto>("کاربری با این شناسه یافت نشد.");

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<ResponseUserDto>(SeatUserMessage);

            _mapper.Map(updateUserDto, user);

            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
            {
                ResponseDto<string> changeRoleResult =
                    await ChangeRoleAsync(
                        new ChangeRoleDto()
                        {
                            UserId = user.Id,
                            RoleIds = updateUserDto.RoleIds
                        },
                        cancellationToken);

                return ResponseDtoHelper
                   .Ok((await GetResponseUserDtoByIdAsync(getUserDto:
                    new GetUserDto()
                    {
                        Id = user.Id,
                    }, cancellationToken: cancellationToken)).Data,
                    changeRoleResult.Data);
            }


            return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(result.DumpErrors());
        }

        public async ValueTask<ResponseDto<ResponseUserDto>> GetResponseUserDtoByIdAsync(GetUserDto getUserDto, CancellationToken cancellationToken)
        {
            if (getUserDto is null)
                return ResponseDtoHelper.BadRequest<ResponseUserDto>(NasbNiroMessages.InvalidData);

            IQueryable<User> query = _userManager.Users.Where(u => !u.UserName.StartsWith("seat."));
            IQueryable<ResponseUserDto> result;

            result = query.ProjectTo<ResponseUserDto>(_mapper.ConfigurationProvider);

            return ResponseDtoHelper.Ok(await
                result.SingleOrDefaultAsync(u => u.Id == getUserDto.Id, cancellationToken));
        }

        public async Task<ResponseDto<bool>> DeleteUserAsync(int userId, CancellationToken cancellationToken)
        {
            User user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user is null)
                return ResponseDtoHelper
                    .NotFound<bool>("کاربری با این شناسه یافت نشد.");

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<bool>(SeatUserMessage);

            if (user.IsDeleted == true)
                return ResponseDtoHelper
                    .BadRequest<bool>("این کاربر قبلا حذف شده است.");

            user.IsDeleted = true;
            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
                return ResponseDtoHelper
                    .Ok(true, "کاربر حذف شد.");

            return ResponseDtoHelper
                    .BadRequest<bool>(result.DumpErrors());
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteUserResultDto>>> DeleteUserCollectionAsync(IEnumerable<int> userIds, CancellationToken cancellationToken)
        {
            if (userIds is null)
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteUserResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteUserResultDto> deleteUserResultDtos = new List<DeleteUserResultDto>();

            foreach (int userId in userIds)
            {
                bool exception = false;
                ResponseDto<bool> result = null;

                try
                {
                    result = await this.DeleteUserAsync(userId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteUserResultDtos.Add(new DeleteUserResultDto()
                    {
                        Id = userId,
                        Success = false,
                        Message = "حذف کاربر با خطا مواجه شد."
                    });

                    continue;
                }

                DeleteUserResultDto deleteContractResultDto
                    = new DeleteUserResultDto() { Id = userId, Success = result.Success, Message = result.Message };

                deleteUserResultDtos.Add(deleteContractResultDto);
            }

            return ResponseDtoHelper
                .Ok(deleteUserResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<string>> AttachProfilePhotoAsync(AttachProfilePhotoDto dto, CancellationToken cancellationToken)
        {

            User user = await _userManager
                .Users
                .Where(x => x.Id == dto.UserId)
                .SingleOrDefaultAsync(x => !x.IsDeleted, cancellationToken);

            if (user is null || user.IsDeleted)
                return ResponseDtoHelper.NotFound<string>(UserMessages.UserNotFound);

            if (user.ProfilePhotoId.HasValue)
            {
                try
                {
                    await _documentService.DeleteDocumentAsync(user.ProfilePhotoId.Value).WithCancellationTokenAsync(cancellationToken);
                }
                catch (Exception ex)
                {

                    _logger.LogError(ex.Message, ex);
                }
            }

            Guid documentId = await _documentService.UploadDocumentAsync(dto).WithCancellationTokenAsync(cancellationToken);

            user.ProfilePhotoId = documentId;

            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
            {
                return ResponseDtoHelper.Ok(documentId.ToString());
            }

            return ResponseDtoHelper.BadRequest<string>(result.DumpErrors());
        }

        public async ValueTask<ResponseDto<bool>> DetachUserProfilePhotoAsync(int userId, CancellationToken cancellationToken)
        {
            User user = await _userManager
                .Users
                .SingleOrDefaultAsync(
                    x => !x.IsDeleted && x.Id == userId,
                    cancellationToken);

            if (user is null)
                return ResponseDtoHelper.NotFound<bool>(UserMessages.UserNotFound);

            if (user.ProfilePhotoId.HasValue)
            {
                bool deleteDocumentResult = await _documentService.DeleteDocumentAsync(
                    user.ProfilePhotoId.Value).WithCancellationTokenAsync(cancellationToken);

                user.ProfilePhotoId = null;
                await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

                return ResponseDtoHelper.Ok(true);
            }
            else
            {
                return ResponseDtoHelper.NotFound<bool>(UserMessages.NoProfilePhotosFound);
            }
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _uow.SaveChangesAsync(cancellationToken) != 0;
        }
    }
}
