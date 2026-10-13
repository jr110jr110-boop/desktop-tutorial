using System.Collections.Generic;
using System.Threading.Tasks;
using ViewModels.Identity.Settings;

namespace Services.Contracts.Identity
{
    public interface ISmsSender
    {
        #region BaseClass

        Task SendSmsAsync(string number, string message);
        bool SendSms(string[] Field, string type);
        Task<bool> SendSmsConfirmCodeAsync(string[] Field, string text);
        Task<bool> SendSmsLineConfirmCodeAsync(string[] Field, string type);
        Task SendWebPushWithImageAsync(WebPushConfig webPushConfig, string title, string message, string imageUrl, string actionUrl);
        Task SendWebPushAsync(WebPushConfig webPushConfig, string token, string title, string message);
        Task SendWebPushWithTokenArrayAsync(WebPushConfig webPushConfig, List<string> token, string title, string message);
        string SendWebPushWithFcm(List<string> token, string title, string message, int flag);
        string SendWebPushWithFcm(List<string> token, string title, string message, dynamic data, int flag);
        #endregion

        #region CustomMethods

        #endregion
    }
}