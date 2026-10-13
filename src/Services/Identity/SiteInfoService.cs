using Common.GuardToolkit;
using Microsoft.Extensions.Options;
using ViewModels.Identity.Settings;

namespace Services.Contracts.Identity
{
    public class SiteInfoService : ISiteInfoService
    {
        private readonly IOptionsSnapshot<SiteSettings> _siteSettings;

        public SiteInfoService(
            IOptionsSnapshot<SiteSettings> siteSettings)
        {
            _siteSettings = siteSettings;
            _siteSettings.ThrowIfNull(nameof(_siteSettings));
        }
        #region BaseClass
        public string GetSiteTitle()
        {
            return _siteSettings.Value.SiteTitle;
        }

        #endregion

        #region CustomMethods

        #endregion
    }
}