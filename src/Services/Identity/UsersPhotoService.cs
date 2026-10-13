using System.IO;
using Common.GuardToolkit;
using Common.IdentityToolkit;
using Entities.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Services.Contracts.Identity;
using ViewModels.Identity.Settings;

namespace Services.Identity
{
    public class UsersPhotoService : IUsersPhotoService
    {
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IOptionsSnapshot<SiteSettings> _siteSettings;

        public UsersPhotoService(
            IHttpContextAccessor contextAccessor,
            IWebHostEnvironment hostingEnvironment,
            IOptionsSnapshot<SiteSettings> siteSettings)
        {
            _contextAccessor = contextAccessor;
            _contextAccessor.ThrowIfNull(nameof(_contextAccessor));

            _hostingEnvironment = hostingEnvironment;
            _hostingEnvironment.ThrowIfNull(nameof(_hostingEnvironment));

            _siteSettings = siteSettings;
            _siteSettings.ThrowIfNull(nameof(_siteSettings));
        }

        public string GetUsersAvatarsFolderPath()
        {
            var usersAvatarsFolder = _siteSettings.Value.UsersAvatarsFolder;
            var uploadsRootFolder = Path.Combine(_hostingEnvironment.WebRootPath, usersAvatarsFolder);
            if (!Directory.Exists(uploadsRootFolder))
            {
                Directory.CreateDirectory(uploadsRootFolder);
            }
            return uploadsRootFolder;
        }

        public void SetUserDefaultPhoto(User user)
        {
            if (!string.IsNullOrWhiteSpace(user.PhotoFileName))
            {
                return;
            }

            var avatarPath = Path.Combine(GetUsersAvatarsFolderPath(), user.PhotoFileName ?? string.Empty);
            if (!File.Exists(avatarPath))
            {
                user.PhotoFileName = _siteSettings.Value.UserDefaultPhoto;
            }
        }

        public string GetUserDefaultPhoto(string photoFileName)
        {
            if (string.IsNullOrWhiteSpace(photoFileName))
            {
                return _siteSettings.Value.UserDefaultPhoto;
            }

            var avatarPath = Path.Combine(GetUsersAvatarsFolderPath(), photoFileName ?? string.Empty);
            return !File.Exists(avatarPath) ? _siteSettings.Value.UserDefaultPhoto : photoFileName;
        }

        public string GetUserPhotoUrl(string photoFileName)
        {
            photoFileName = GetUserDefaultPhoto(photoFileName);
            return $"~/{_siteSettings.Value.UsersAvatarsFolder}/{photoFileName}";
        }
        public string GetFileApi()
        {
            return $"{_siteSettings.Value.UsersAvatarsApiBase}";
        }
        public string GetUserPhotoApi(string photoFileName)
        {
            return $"{_siteSettings.Value.UsersAvatarsApiBase}?DocumentId={photoFileName}";
        }
        public string GetCurrentUserPhotoUrl()
        {
            var photoFileName = _contextAccessor.HttpContext.User.Identity.GetUserClaimValue(ApplicationClaimsPrincipalFactory.PhotoFileName);
            return GetUserPhotoUrl(photoFileName);
        }

    }
}
