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
        ValueTask<ResponseDto<ResponseUserDto>> RegisterAsync(RegisterUserDto registerUserDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseLoginUserDto>> LoginAsync(LoginUserDto loginUserDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseLoginUserDto>> RefreshTokenAsync(RefreshTokenDto refreshTokenDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseLoginUserDto>> ChangePasswordAsync(ChangePasswordDto changePasswordDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<bool>> LogoutAsync(string userId, string refreshToken, CancellationToken cancellationToken);
        ValueTask<ResponseDto<string>> ChangeRoleAsync(ChangeRoleDto changeRoleDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<ResponseRole>>> GetAllRolesAsync(CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseUserDto>> GetUserInfoAsync(CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseUserDto>>> GetAllUsersAsync(UserPaginationParams userPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseUserShortListDto>> GetEngineerUsersAsync(CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseUserDto>> UpdateUserAsync(UpdateUserDto updateUserDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseUserDto>> GetResponseUserDtoByIdAsync(GetUserDto getUserDto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteUserAsync(int userId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteUserResultDto>>> DeleteUserCollectionAsync(IEnumerable<int> userIds, CancellationToken cancellationToken);
        Task<ResponseDto<ResponseDashboardUserDto>> GetUserDashboardInfoAsync(CancellationToken cancellationToken);
        ValueTask<ResponseDto<string>> AttachProfilePhotoAsync(AttachProfilePhotoDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<bool>> DetachUserProfilePhotoAsync(int userId, CancellationToken cancellationToken);
    }
}
