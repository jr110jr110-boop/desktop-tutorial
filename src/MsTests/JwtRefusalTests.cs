using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebApi.Extensions;

namespace MsTests
{
    /// <summary>
    /// R1a: a refusal of the bearer handler (no valid token: 401, role attribute not satisfied: 403) answers the same ResponseDto JSON as the services.
    /// The real <c>JwtBearerHandler</c> runs here (challenge and forbid are what the authorization middleware calls); the two events are the ones
    /// Startup assigns. The whole HTTP pipeline needs a SQL Server, so the smoke script checks the end-to-end answer.
    /// </summary>
    [TestClass]
    public class JwtRefusalTests
    {
        private const string Key = "unit-test-signing-key-0123456789-abcdefghijklmnopq";

        private sealed class Answer
        {
            public int Status;
            public string ContentType;
            public string Body;
            public string WwwAuthenticate;
            public JObject Json => JObject.Parse(Body);
        }

        private static ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(cfg =>
                {
                    cfg.RequireHttpsMetadata = false;
                    cfg.MapInboundClaims = false;
                    cfg.TokenValidationParameters = new TokenValidationParameters
                    {
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                        ValidateIssuerSigningKey = true,
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,
                    };
                    // the same two assignments as in Startup (the account check needs the database and is not part of this test)
                    cfg.Events = new JwtBearerEvents
                    {
                        OnChallenge = JwtBearerRefusals.OnChallengeAsync,
                        OnForbidden = JwtBearerRefusals.OnForbiddenAsync,
                    };
                });
            return services.BuildServiceProvider();
        }

        private static async Task<Answer> RunAsync(Func<HttpContext, Task> action, string authorization = null)
        {
            using ServiceProvider provider = BuildProvider();
            using IServiceScope scope = provider.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var body = new MemoryStream();
            http.Response.Body = body;
            if (authorization != null) http.Request.Headers["Authorization"] = authorization;

            await action(http);

            return new Answer
            {
                Status = http.Response.StatusCode,
                ContentType = http.Response.ContentType,
                Body = Encoding.UTF8.GetString(body.ToArray()),
                WwwAuthenticate = http.Response.Headers["WWW-Authenticate"].ToString(),
            };
        }

        private static string Token(DateTime notBefore, DateTime expires)
        {
            var handler = new JwtSecurityTokenHandler();
            SecurityToken token = handler.CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.UserData, "7") }),
                NotBefore = notBefore,
                Expires = expires,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256Signature),
            });
            return handler.WriteToken(token);
        }

        private static void AssertStandardRefusal(Answer answer, int status, string message)
        {
            Assert.AreEqual(status, answer.Status);
            Assert.AreEqual("application/json; charset=utf-8", answer.ContentType);
            JObject json = answer.Json;
            Assert.AreEqual(false, (bool)json["success"]);
            Assert.AreEqual(message, (string)json["message"]);
            Assert.AreEqual(JTokenType.Null, json["data"].Type, "data is written as null, not left out");
            Assert.AreEqual(status, (int)json["httpStatusCode"]);
            CollectionAssert.AreEquivalent(new[] { "success", "message", "data", "httpStatusCode" }, json.Properties().Select(p => p.Name).ToArray(),
                "exactly the four properties of ResponseDto, in camelCase");
        }

        [TestMethod]
        public async Task ARoleRefusalAnswersTheStandardJsonWith403()
        {
            Answer answer = await RunAsync(http => http.ForbidAsync(JwtBearerDefaults.AuthenticationScheme));

            AssertStandardRefusal(answer, 403, JwtBearerRefusals.NotPermitted);
            // the word that the smoke helper (and a reader of the refusal) looks for, as in the refusals of the services
            StringAssert.Contains(answer.Json.Value<string>("message"), "مجوز");
        }

        [TestMethod]
        public async Task ARequestWithoutATokenAnswersTheStandardJsonWith401AndTheBearerChallenge()
        {
            Answer answer = await RunAsync(http => http.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme));

            AssertStandardRefusal(answer, 401, JwtBearerRefusals.SignInRequired);
            Assert.AreEqual("Bearer", answer.WwwAuthenticate, "no token was sent, so the challenge carries no error, as the handler writes it");
        }

        [TestMethod]
        public async Task AnInvalidTokenKeepsTheErrorOfTheHandlerInTheChallengeHeader()
        {
            Answer answer = await RunAsync(http => http.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme), "Bearer not-a-token");

            AssertStandardRefusal(answer, 401, JwtBearerRefusals.SignInRequired);
            StringAssert.StartsWith(answer.WwwAuthenticate, "Bearer");
            StringAssert.Contains(answer.WwwAuthenticate, "error=\"invalid_token\"");
        }

        [TestMethod]
        public async Task AnExpiredTokenKeepsTheErrorDescriptionInTheChallengeHeader()
        {
            string expired = Token(DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-5));

            Answer answer = await RunAsync(http => http.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme), "Bearer " + expired);

            AssertStandardRefusal(answer, 401, JwtBearerRefusals.SignInRequired);
            StringAssert.Contains(answer.WwwAuthenticate, "error=\"invalid_token\"");
            StringAssert.Contains(answer.WwwAuthenticate.ToLowerInvariant(), "expired");
        }

        [TestMethod]
        public void TheBodyIsTheSameTextAsTheAnswerOfAService()
        {
            // a service answers ResponseDtoHelper.Forbidden(...) through MVC with the settings ApiJson.Apply gives it (Startup.AddNewtonsoftJson)
            string service = JsonConvert.SerializeObject(ResponseDtoHelper.Forbidden<object>(JwtBearerRefusals.NotPermitted), ApiJson.Create());

            Assert.AreEqual("{\"success\":false,\"message\":\"" + JwtBearerRefusals.NotPermitted + "\",\"data\":null,\"httpStatusCode\":403}", service);
        }

        [TestMethod]
        public async Task TheBodyOfARefusalIsByteIdenticalToTheAnswerOfAService()
        {
            Answer forbidden = await RunAsync(http => http.ForbidAsync(JwtBearerDefaults.AuthenticationScheme));
            Answer challenged = await RunAsync(http => http.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme));

            Assert.AreEqual(JsonConvert.SerializeObject(ResponseDtoHelper.Forbidden<object>(JwtBearerRefusals.NotPermitted), ApiJson.Create()), forbidden.Body);
            Assert.AreEqual(JsonConvert.SerializeObject(ResponseDtoHelper.Unauthorized<object>(JwtBearerRefusals.SignInRequired), ApiJson.Create()), challenged.Body);
        }
    }
}
