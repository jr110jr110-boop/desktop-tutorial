using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Identity.Constants;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro;
using ViewModels.NasbNiro.Pagination;

namespace Services.Identity.AccountServices
{
    public interface IAccountService
    {
        /// <summary>PMO only.</summary>
        ValueTask<ResponseDto<ResponseUserDto>> RegisterAsync(RegisterUserDto registerUserDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseLoginUserDto>> LoginAsync(LoginUserDto loginUserDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseLoginUserDto>> RefreshTokenAsync(RefreshTokenDto refreshTokenDto, CancellationToken cancellationToken);

        /// <summary>A user changes his or her own password (current password required).</summary>
        ValueTask<ResponseDto<ResponseLoginUserDto>> ChangePasswordAsync(ChangePasswordDto changePasswordDto, CancellationToken cancellationToken);

        /// <summary>PMO only: sets a temporary password that the user must change at the next sign-in. Returns no tokens.</summary>
        ValueTask<ResponseDto<AdminResetPasswordResultDto>> AdminResetPasswordAsync(AdminResetPasswordDto dto, CancellationToken cancellationToken);

        /// <summary>Anonymous: the user proves the temporary password and chooses a new one.</summary>
        ValueTask<ResponseDto<ResponseLoginUserDto>> ChangeTemporaryPasswordAsync(ChangeTemporaryPasswordDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<bool>> LogoutAsync(string userId, string refreshToken, CancellationToken cancellationToken);

        /// <summary>PMO only; nobody changes his or her own roles; a sensitive role becomes a request for a second PMO user to approve.</summary>
        ValueTask<ResponseDto<string>> ChangeRoleAsync(ChangeRoleDto changeRoleDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<ResponseRole>>> GetAllRolesAsync(CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseUserDto>> GetUserInfoAsync(CancellationToken cancellationToken);

        /// <summary>PMO only (the full record of every user).</summary>
        ValueTask<ResponseDto<PagedList<ResponseUserDto>>> GetAllUsersAsync(UserPaginationParams userPaginationParams, CancellationToken cancellationToken);

        /// <summary>Every signed-in user: id, user name and name of the active users, for pickers.</summary>
        ValueTask<ResponseDto<PagedList<ResponseUserLookupDto>>> GetUserLookupAsync(UserPaginationParams userPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseUserShortListDto>> GetEngineerUsersAsync(CancellationToken cancellationToken);

        /// <summary>PMO only.</summary>
        ValueTask<ResponseDto<ResponseUserDto>> UpdateUserAsync(UpdateUserDto updateUserDto, CancellationToken cancellationToken);

        /// <summary>The signed-in user's own record, or any user's record for the PMO.</summary>
        ValueTask<ResponseDto<ResponseUserDto>> GetResponseUserDtoByIdAsync(GetUserDto getUserDto, CancellationToken cancellationToken);

        /// <summary>PMO only; not the own account.</summary>
        Task<ResponseDto<bool>> DeleteUserAsync(int userId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteUserResultDto>>> DeleteUserCollectionAsync(IEnumerable<int> userIds, CancellationToken cancellationToken);
        Task<ResponseDto<ResponseDashboardUserDto>> GetUserDashboardInfoAsync(CancellationToken cancellationToken);

        /// <summary>The own photo, or any photo for the PMO.</summary>
        ValueTask<ResponseDto<string>> AttachProfilePhotoAsync(AttachProfilePhotoDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<bool>> DetachUserProfilePhotoAsync(int userId, CancellationToken cancellationToken);

        /// <summary>PMO only: the sensitive roles that wait for a second PMO user.</summary>
        ValueTask<ResponseDto<List<RoleGrantDto>>> GetRoleGrantsAsync(CancellationToken cancellationToken);

        /// <summary>PMO only; the approver is not the one who asked and not the one who receives the role.</summary>
        ValueTask<ResponseDto<RoleGrantDto>> ApproveRoleGrantAsync(int grantId, CancellationToken cancellationToken);

        /// <summary>PMO only: rejects a request (or the one who asked takes it back).</summary>
        ValueTask<ResponseDto<bool>> RejectRoleGrantAsync(int grantId, CancellationToken cancellationToken);
    }
}
