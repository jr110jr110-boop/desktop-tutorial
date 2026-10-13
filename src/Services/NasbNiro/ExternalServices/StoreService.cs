using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Common.Extentions;
using Common.GuardToolkit;
using Common.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Services.NasbNiro.ExternalServices
{
    public sealed class StoreService
    {
        private readonly ILogger<StoreService> _logger;
        private readonly HttpClient _client;

        private string _token { get; }
        private string _goodCountAddress { get; }


        public StoreService(IConfiguration configurations, ILogger<StoreService> logger)
        {
            configurations.ThrowIfNull(nameof(configurations));

            _token = configurations.GetValue<string>("StoreService:Token").LogInfo(logger);

            _goodCountAddress = configurations
                .GetValue<string>("StoreService:GoodCountAddress")
                .EndWithIfNot('/')
                .LogInfo(logger);

            _logger = logger;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_token}");

        }

        public async Task<ResultExceptional<int>> GetGoodCountAsync(string goodCode, CancellationToken cancellationToken)
        {
            goodCode.ThrowIfNull(nameof(goodCode));

            var (errorMessage, result) = await GuardExt.RunSafeAsync(
               operation: async () =>
               {
                   HttpResponseMessage response = await _client.PostAsync(
                      requestUri: $"{_goodCountAddress}{goodCode}",
                      null,
                      cancellationToken);

                   if (response.IsSuccessStatusCode)
                   {
                       string apiResult = await response.Content.ReadAsStringAsync();
                       int count = JsonConvert.DeserializeObject<object[]>(apiResult).Count();

                       return count;
                   }


                   throw new Exception($"request failed : status code :> {response.StatusCode}");
               },
               exceptionHandler: (ex) =>
               {
                   _logger.LogError(ex, ex.Message);
               },
               cancellationToken);

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return new ResultExceptional<int>(new Exception(errorMessage));
            }

            return new ResultExceptional<int>(result);
        }
    }
}
