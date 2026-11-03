using System;
using System.Linq;
using System.Threading.Tasks;
using Common.GuardToolkit;
using DataLayer.Context;
using Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Services.Token
{
    public interface ITokenStoreService
    {
        Task AddUserTokenAsync(CustomUserToken userToken);
        Task AddUserTokenAsync(User user, string refreshTokenSerial, string accessToken, string refreshTokenSourceSerial);
        Task<bool> IsValidTokenAsync(string accessToken, int userId);
        Task DeleteExpiredTokensAsync();
        Task<CustomUserToken> FindTokenAsync(string refreshTokenValue);
        Task DeleteTokenAsync(string refreshTokenValue);
        Task DeleteTokensWithSameRefreshTokenSourceAsync(string refreshTokenIdHashSource);
        /// <summary>Removes every refresh token record of the user. With <paramref name="keepConsumed"/> the records of refresh tokens that were already used stay (they are the tripwire for a reuse) unless they have run out.</summary>
        Task InvalidateUserTokensAsync(int userId, bool keepConsumed = false);
        Task RevokeUserBearerTokensAsync(string userIdValue, string refreshTokenValue);

        /// <summary>R1a2 (L3): a refresh token that was used is not deleted but marked as used (and when), so presenting it again is recognised.</summary>
        void MarkConsumed(CustomUserToken token, DateTimeOffset now);

        /// <summary>True for the record of a refresh token that was used.</summary>
        bool IsConsumed(CustomUserToken token);
    }

    public class TokenStoreService : ITokenStoreService
    {
        private readonly ISecurityService _securityService;
        private readonly IUnitOfWork _uow;
        private readonly DbSet<CustomUserToken> _tokens;
        private readonly IOptionsSnapshot<BearerTokensOptions> _configuration;
        private readonly ITokenFactoryService _tokenFactoryService;

        public TokenStoreService(
            IUnitOfWork uow,
            ISecurityService securityService,
            IOptionsSnapshot<BearerTokensOptions> configuration,
            ITokenFactoryService tokenFactoryService)
        {
            _uow = uow;
            _uow.ThrowIfNull(nameof(_uow));

            _securityService = securityService;
            _securityService.ThrowIfNull(nameof(_securityService));

            _tokens = _uow.Set<CustomUserToken>();

            _configuration = configuration;
            _configuration.ThrowIfNull(nameof(configuration));

            _tokenFactoryService = tokenFactoryService;
            _tokenFactoryService.ThrowIfNull(nameof(tokenFactoryService));
        }

        /// <summary>The access-token hash of a record whose refresh token was used (no real hash is empty). The time of the use is kept in AccessTokenExpiresDateTime, which a used record does not need otherwise.</summary>
        private const string ConsumedMark = "";

        public void MarkConsumed(CustomUserToken token, DateTimeOffset now)
        {
            token.AccessTokenHash = ConsumedMark;
            token.AccessTokenExpiresDateTime = now;
        }

        public bool IsConsumed(CustomUserToken token) => token != null && token.AccessTokenHash == ConsumedMark;

        public async Task AddUserTokenAsync(CustomUserToken userToken)
        {
            if (!_configuration.Value.AllowMultipleLoginsFromTheSameUser)
            {
                // one session per user: the other refresh tokens go, but the used ones stay as the tripwire of a reuse
                await InvalidateUserTokensAsync(userToken.UserId, keepConsumed: true);
            }
            await DeleteTokensWithSameRefreshTokenSourceAsync(userToken.RefreshTokenIdHashSource);
            _tokens.Add(userToken);
        }

        public async Task AddUserTokenAsync(User user, string refreshTokenSerial, string accessToken, string refreshTokenSourceSerial)
        {
            var now = DateTimeOffset.UtcNow;
            var token = new CustomUserToken
            {
                UserId = user.Id,
                // Refresh token handles should be treated as secrets and should be stored hashed
                RefreshTokenIdHash = _securityService.GetSha256Hash(refreshTokenSerial),
                RefreshTokenIdHashSource = string.IsNullOrWhiteSpace(refreshTokenSourceSerial) ?
                                           null : _securityService.GetSha256Hash(refreshTokenSourceSerial),
                AccessTokenHash = _securityService.GetSha256Hash(accessToken),
                RefreshTokenExpiresDateTime = now.AddMinutes(_configuration.Value.RefreshTokenExpirationMinutes),
                AccessTokenExpiresDateTime = now.AddMinutes(_configuration.Value.AccessTokenExpirationMinutes)
            };
            await AddUserTokenAsync(token);
        }

        public async Task DeleteExpiredTokensAsync()
        {
            var now = DateTimeOffset.UtcNow;
            await _tokens.Where(x => x.RefreshTokenExpiresDateTime < now)
                         .ForEachAsync(userToken =>
                         {
                             _tokens.Remove(userToken);
                         });
        }

        public async Task DeleteTokenAsync(string refreshTokenValue)
        {
            var token = await FindTokenAsync(refreshTokenValue);
            if (token != null)
            {
                _tokens.Remove(token);
            }
        }

        public async Task DeleteTokensWithSameRefreshTokenSourceAsync(string refreshTokenIdHashSource)
        {
            if (string.IsNullOrWhiteSpace(refreshTokenIdHashSource))
            {
                return;
            }
            await _tokens.Where(t => t.RefreshTokenIdHashSource == refreshTokenIdHashSource)
                         .ForEachAsync(userToken =>
                         {
                             _tokens.Remove(userToken);
                         });
        }

        public async Task RevokeUserBearerTokensAsync(string userIdValue, string refreshTokenValue)
        {
            if (!string.IsNullOrWhiteSpace(userIdValue) && int.TryParse(userIdValue, out int userId))
            {
                if (_configuration.Value.AllowSignoutAllUserActiveClients)
                {
                    await InvalidateUserTokensAsync(userId);
                }
            }

            if (!string.IsNullOrWhiteSpace(refreshTokenValue))
            {
                var refreshTokenSerial = _tokenFactoryService.GetRefreshTokenSerial(refreshTokenValue);
                if (!string.IsNullOrWhiteSpace(refreshTokenSerial))
                {
                    var refreshTokenIdHashSource = _securityService.GetSha256Hash(refreshTokenSerial);
                    await DeleteTokensWithSameRefreshTokenSourceAsync(refreshTokenIdHashSource);
                    // R1a2: the presented token itself goes too (only its children were removed before, so a signed-out refresh token still worked)
                    await DeleteTokenAsync(refreshTokenValue);
                }
            }

            await DeleteExpiredTokensAsync();
        }

        public Task<CustomUserToken> FindTokenAsync(string refreshTokenValue)
        {
            // R1a: these used to return a null Task, so awaiting the answer for a malformed refresh token threw a NullReferenceException (an HTTP 500).
            if (string.IsNullOrWhiteSpace(refreshTokenValue))
            {
                return Task.FromResult<CustomUserToken>(null);
            }

            var refreshTokenSerial = _tokenFactoryService.GetRefreshTokenSerial(refreshTokenValue);
            if (string.IsNullOrWhiteSpace(refreshTokenSerial))
            {
                return Task.FromResult<CustomUserToken>(null);
            }

            var refreshTokenIdHash = _securityService.GetSha256Hash(refreshTokenSerial);
            return _tokens.Include(x => x.User).ThenInclude(x => x.UserProfile).FirstOrDefaultAsync(x => x.RefreshTokenIdHash == refreshTokenIdHash);
        }

        public async Task InvalidateUserTokensAsync(int userId, bool keepConsumed = false)
        {
            var now = DateTimeOffset.UtcNow;
            await _tokens.Where(x => x.UserId == userId)
                         .ForEachAsync(userToken =>
                         {
                             // a used record is only a tripwire until its refresh token would have run out anyway
                             if (keepConsumed && IsConsumed(userToken) && userToken.RefreshTokenExpiresDateTime >= now) return;
                             _tokens.Remove(userToken);
                         });
        }

        public async Task<bool> IsValidTokenAsync(string accessToken, int userId)
        {
            var accessTokenHash = _securityService.GetSha256Hash(accessToken);
            var userToken = await _tokens.FirstOrDefaultAsync(
                x => x.AccessTokenHash == accessTokenHash && x.UserId == userId);
            return userToken?.AccessTokenExpiresDateTime >= DateTimeOffset.UtcNow;
        }
    }
}