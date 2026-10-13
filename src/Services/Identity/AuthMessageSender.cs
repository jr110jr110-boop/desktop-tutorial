using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Services.Contracts.Identity;
using ViewModels.Identity.Settings;

namespace Services.Identity
{
    /// <summary>
    /// More info: http://www.dotnettips.info/post/2551
    /// And http://www.dotnettips.info/post/2564
    /// </summary>
    public class AuthMessageSender : IEmailSender, ISmsSender
    {
        private readonly IOptionsSnapshot<SiteSettings> _smtpConfig;
        private static readonly HttpClient client = new HttpClient();

        public AuthMessageSender(IOptionsSnapshot<SiteSettings> smtpConfig)
        {
            _smtpConfig = smtpConfig;
            _smtpConfig.ThrowIfNull(nameof(_smtpConfig));
        }

        public Task SendEmailAsync<T>(string email, string subject, string viewNameOrPath, T model)
        {
            // Razor e-mail templates were rendered by DNTCommon.Web.Core, but this API project contains no
            // .cshtml templates, so a template based e-mail could not be produced before either.
            throw new NotSupportedException(
                $"E-mail template '{viewNameOrPath}' is not available in this API. Use SendEmailAsync(email, subject, message).");
        }

        public async Task SendEmailAsync(string email, string subject, string message)
        {
            // Sends through System.Net.Mail (replaces DNTCommon.Web.Core's IWebMailService).
            var smtp = _smtpConfig.Value.Smtp;
            smtp.ThrowIfNull(nameof(smtp));

            using (var mailMessage = new System.Net.Mail.MailMessage())
            {
                mailMessage.From = new System.Net.Mail.MailAddress(smtp.FromAddress, smtp.FromName, Encoding.UTF8);
                mailMessage.To.Add(new System.Net.Mail.MailAddress(email));
                mailMessage.Subject = subject;
                mailMessage.SubjectEncoding = Encoding.UTF8;
                mailMessage.Body = message;
                mailMessage.BodyEncoding = Encoding.UTF8;
                mailMessage.IsBodyHtml = true;

                using (var smtpClient = new System.Net.Mail.SmtpClient())
                {
                    if (smtp.UsePickupFolder && !string.IsNullOrWhiteSpace(smtp.PickupFolder))
                    {
                        if (!Directory.Exists(smtp.PickupFolder))
                        {
                            Directory.CreateDirectory(smtp.PickupFolder);
                        }

                        smtpClient.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.SpecifiedPickupDirectory;
                        smtpClient.PickupDirectoryLocation = smtp.PickupFolder;
                    }
                    else
                    {
                        smtpClient.Host = smtp.Server;
                        smtpClient.Port = smtp.Port;
                        smtpClient.EnableSsl = smtp.EnableSsl;
                        if (!string.IsNullOrWhiteSpace(smtp.Username))
                        {
                            smtpClient.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
                        }
                    }

                    await smtpClient.SendMailAsync(mailMessage);
                }
            }
        }

        public Task SendSmsAsync(string number, string message)
        {
            // Plug in your SMS service here to send a text message.
            return Task.FromResult(0);
        }

        public bool SendSms(string[] Field, string template)
        {
            throw new NotImplementedException(nameof(SendSms));
        }

        public async Task<bool> SendSmsConfirmCodeAsync(string[] Field, string text)
        {
            try
            {
                var parameters = new
                {
                    Username = Field[0],
                    password = Field[1],
                    Providerid = Field[2],
                    ToNumber = Field[3],
                    SMSText = text
                };
                var result = await CallApiAsync<string>("http://sms.bimc.com/MsgService.svc/SendSMSRest", parameters);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SendSmsLineConfirmCodeAsync(string[] Field, string text)
        {
            var parameters = new
            {
                UserName = Field[1],
                PassWord = Field[2],
                text = text,
                to = Field[0],
                from = Field[3],
            };
            var result = await CallApiAsync<string>("https://rest.payamak-panel.com/api/SendSMS/SendSMS", parameters);
            return true;
        }
        public async Task<bool> SendSmsLineShareConfirmCodeAsync(string[] Field, string type)
        {
            var parameters = new
            {
                UserName = Field[1],
                PassWord = Field[2],
                text = type,
                to = Field[0],
                bodyId = 34491
            };
            var result = await CallApiAsync<string>("https://rest.payamak-panel.com/api/SendSMS/BaseServiceNumber", parameters);
            return true;
        }
        public async Task SendWebPushAsync(WebPushConfig webPushConfig, string token, string title, string message)
        {
            var tokenList = new List<string>();
            tokenList.Add(token);
            var parameters = new
            {
                api_key = webPushConfig.api_key,
                subscriber_tokens = tokenList,
                title = title,
                body = message,
                onclick_action = "",
                url = "http://https://leadzinc.ir/",
                content = "some content",
                icon = webPushConfig.icon,
                sent_time = DateTime.Now.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            };
            var result = await CallApiAsync<string>("https://app.najva.com/notification/api/v1/notifications/", webPushConfig.token, parameters);
        }

        public async Task SendWebPushWithTokenArrayAsync(WebPushConfig webPushConfig, List<string> token, string title, string message)
        {

            var parameters = new
            {
                api_key = webPushConfig.api_key,
                subscriber_tokens = token,
                title = title,
                body = message,
                onclick_action = "",
                url = "http://https://leadzinc.ir/",
                content = "some content",
                icon = webPushConfig.icon,
                sent_time = DateTime.Now.AddSeconds(15).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            };
            var result = await CallApiAsync<string>("https://app.najva.com/notification/api/v1/notifications/", webPushConfig.token, parameters);

        }
        public static async Task<string> GetCallApiAsync<T>(string apiUrl)
        {
            string apiCallResult = "";
            using (var client = new HttpClient())
            {
                try
                {
                    client.BaseAddress = new Uri(apiUrl);
                    var w = client.GetAsync(apiUrl);
                    await w;
                    HttpResponseMessage response = w.Result;
                    var result = response.Content.ReadAsStringAsync();
                    await result;
                    apiCallResult = result.Result;
                    //if (response.IsSuccessStatusCode)
                    //{

                    //}
                }
                catch (AggregateException e)
                {
                    apiCallResult = e.Message.ToString();
                }
                return apiCallResult;
            }
        }
        public static async Task<string> CallApiAsync<T>(string apiUrl, object value)
        {
            var myContent = JsonConvert.SerializeObject(value);
            var content = new StringContent(myContent, Encoding.UTF8, "application/json");
            string apiCallResult = "";
            using (var client = new HttpClient())
            {
                try
                {
                    client.BaseAddress = new Uri(apiUrl);
                    var w = client.PostAsync(apiUrl, content);
                    await w;
                    HttpResponseMessage response = w.Result;
                    var result = response.Content.ReadAsStringAsync();
                    await result;
                    apiCallResult = result.Result;
                    //if (response.IsSuccessStatusCode)
                    //{

                    //}
                }
                catch (AggregateException e)
                {
                    apiCallResult = e.Message.ToString();
                }
                return apiCallResult;
            }
        }
        public static async Task<string> CallApiAsync<T>(string apiUrl, string header, object value)
        {
            //ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;

            var myContent = JsonConvert.SerializeObject(value);
            var content = new StringContent(myContent, Encoding.UTF8, "application/json");
            string apiCallResult = "";
            using (var client = new HttpClient())
            {
                try
                {
                    client.BaseAddress = new Uri(apiUrl);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", header);
                    HttpResponseMessage response = await client.PostAsync(apiUrl, content);

                    apiCallResult = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {

                    }
                }
                catch (AggregateException e)
                {
                    apiCallResult = e.Message.ToString();
                }
                return apiCallResult;
            }
        }

        public string SendWebPushWithFcm(List<string> token, string title, string message, int flag = 0)
        {
            try
            {
                if (token.Count > 0)
                {
                    // توکن
                    string regIds = string.Join("\",\"", token);
                    WebPushConfig webPushConfig = new WebPushConfig
                    {
                        webPushFcmSenderId = _smtpConfig.Value.FcmSenderId,
                        webPushFcmServerKey = _smtpConfig.Value.FcmServerKey,
                        icon = ""
                    };
                    string AppId = webPushConfig.webPushFcmServerKey;
                    string SenderId = webPushConfig.webPushFcmSenderId;

                    WebRequest wRequest;
                    wRequest = WebRequest.Create("https://fcm.googleapis.com/fcm/send");
                    wRequest.Method = "post";
                    wRequest.ContentType = " application/json;charset=UTF-8";
                    wRequest.Headers.Add(string.Format("Authorization: key={0}", AppId));

                    wRequest.Headers.Add(string.Format("Sender: id={0}", SenderId));

                    string datetime = System.DateTime.Now.ToString();
                    string strDateTime = datetime.Substring(0, datetime.Length - 4);
                    //string postData = "{\"collapse_key\":\"score_update\",\"time_to_live\":108,\"delay_while_idle\":true,\"data\": { \"title\" : " + "\"" + title + "\",\"message\" : " + "\"" + message + "\",\"date\": " + "\"" + strDateTime + "\"},\"registration_ids\":[\"" + regIds + "\"]}";
                    //string postData = "{" +
                    //    "\"collapse_key\":\"score_update\"," +
                    //    "\"time_to_live\":108," +
                    //    "\"delay_while_idle\":true," +
                    //    "\"data\": { \"jsonData\" : " +
                    //    data + ",\"flag\" : " + flag + "," +
                    //    "\"date\": " + "\"" + strDateTime + "\"}," +
                    //    "\"registration_ids\":[\"" + regIds + "\"]}";

                    string postData = "{\"collapse_key\":\"score_update\",\"time_to_live\":108,\"delay_while_idle\":true,\"notification\": { \"title\" : " + "\"" + title + "\",\"body\" : " + "\"" + message + "\",\"icon\" : " + "\"" + webPushConfig.icon + "\",\"time\": " + "\"" + strDateTime + "\"},\"registration_ids\":[\"" + regIds + "\"]}";

                    Byte[] bytes = Encoding.UTF8.GetBytes(postData);
                    wRequest.ContentLength = bytes.Length;

                    Stream stream = wRequest.GetRequestStream();
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Close();

                    WebResponse wResponse = wRequest.GetResponse();

                    stream = wResponse.GetResponseStream();

                    StreamReader reader = new StreamReader(stream);

                    String response = reader.ReadToEnd();

                    HttpWebResponse httpResponse = (HttpWebResponse)wResponse;
                    string status = httpResponse.StatusCode.ToString();

                    reader.Close();
                    stream.Close();
                    wResponse.Close();

                    if (status == "")
                    {
                        return response;
                    }
                    else
                    {
                        return "";
                    }
                }
                else
                {
                    return "";
                }
            }
            catch (Exception)
            {
                return "";
            }

        }
        public string SendWebPushWithFcm(List<string> token, string title, string message, dynamic data, int flag = 0)
        {
            try
            {
                if (token.Count > 0)
                {
                    // توکن
                    string regIds = string.Join("\",\"", token);
                    WebPushConfig webPushConfig = new WebPushConfig
                    {
                        webPushFcmSenderId = _smtpConfig.Value.FcmSenderId,
                        webPushFcmServerKey = _smtpConfig.Value.FcmServerKey,
                        icon = ""
                    };
                    string AppId = webPushConfig.webPushFcmServerKey;
                    string SenderId = webPushConfig.webPushFcmSenderId;

                    WebRequest wRequest;
                    wRequest = WebRequest.Create("https://fcm.googleapis.com/fcm/send");
                    wRequest.Method = "post";
                    wRequest.ContentType = " application/json;charset=UTF-8";
                    wRequest.Headers.Add(string.Format("Authorization: key={0}", AppId));

                    wRequest.Headers.Add(string.Format("Sender: id={0}", SenderId));

                    string datetime = System.DateTime.Now.ToString();
                    string strDateTime = datetime.Substring(0, datetime.Length - 4);

                    string postData = "{\"data\":" + data +
                        ",\"registration_ids\":[\"" + regIds + "\"]}";

                    Byte[] bytes = Encoding.UTF8.GetBytes(postData);
                    wRequest.ContentLength = bytes.Length;

                    Stream stream = wRequest.GetRequestStream();
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Close();

                    WebResponse wResponse = wRequest.GetResponse();

                    stream = wResponse.GetResponseStream();

                    StreamReader reader = new StreamReader(stream);

                    String response = reader.ReadToEnd();

                    HttpWebResponse httpResponse = (HttpWebResponse)wResponse;
                    string status = httpResponse.StatusCode.ToString();

                    reader.Close();
                    stream.Close();
                    wResponse.Close();

                    if (status == "")
                    {
                        return response;
                    }
                    else
                    {
                        return "";
                    }
                }
                else
                {
                    return "";
                }
            }
            catch
            {
                return "";
            }

        }
        public async Task SendWebPushWithImageAsync(WebPushConfig webPushConfig, string title, string message, string imageUrl, string actionUrl)
        {
            var parameters = new
            {
                api_key = webPushConfig.api_key,
                title = title,
                body = message,
                image = imageUrl,
                onclick_action = "open-link",
                url = actionUrl,
                content = "",
                icon = webPushConfig.icon,
                sent_time = DateTime.Now.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                segments_include = JArray.FromObject(new List<dynamic>()),
                segments_exclude = JArray.FromObject(new List<dynamic>()),
                one_signal_enabled = false,
                one_signal_accounts = JArray.FromObject(new List<dynamic>()),
            };
            var result = await CallApiAsync<string>("https://app.najva.com/api/v1/notifications/", webPushConfig.token, parameters);
        }
    }
}