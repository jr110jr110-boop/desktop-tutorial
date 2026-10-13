using Entities.Identity;

namespace Services.Contracts.Identity
{
    public interface IUsersPhotoService
    {
        string GetUsersAvatarsFolderPath();
        void SetUserDefaultPhoto(User user);
        string GetUserDefaultPhoto(string photoFileName);
        string GetUserPhotoUrl(string photoFileName);
        string GetUserPhotoApi(string photoFileName);
        string GetCurrentUserPhotoUrl();
        string GetFileApi();
    }
}