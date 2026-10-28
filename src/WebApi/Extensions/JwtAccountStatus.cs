using System;
using System.Linq;
using System.Threading.Tasks;
using Common.IdentityToolkit;
using DataLayer.Context;
using Entities.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Services.Identity.AccountServices;
using Services.Token;

namespace WebApi.Extensions
{
    /// <summary>
    /// Runs after a bearer token passed its signature, lifetime, issuer and audience checks (R1a, widened in R1a2).
    /// The token is valid only when
    ///  - it is an access token (it carries a user id; a refresh token does not),
    ///  - the account still exists, is active and is not deleted (a deleted or deactivated user is cut off at once, not when the token expires),
    ///  - the token was made for the security stamp the user has now (H2: a password change or reset, a change of roles, a deletion or a logout rotates the stamp,
    ///    so every token made before it stops working at once),
    ///  - the account is not waiting for a new password (the "must change the password" flag): such a user gets no token at sign-in, so a token that
    ///    still exists was made before the flag was set; only the calls that finish the change (and the logout) are let through.
    /// One small lookup of the user by primary key per request (the three facts come back in the same query).
    /// </summary>
    public static class JwtAccountStatus
    {
        /// <summary>What the lookup of the user returns.</summary>
        public sealed class Status
        {
            public bool IsActive { get; set; }
            public bool IsDeleted { get; set; }
            public string SecurityStamp { get; set; }
            public bool MustChangePassword { get; set; }
        }

        /// <summary>The calls a user who must change the password may still make with an old token.</summary>
        private static bool IsPasswordChangeOrLogout(string path) =>
            !string.IsNullOrEmpty(path) &&
            (path.IndexOf("/accounts/change-temporary-password", StringComparison.OrdinalIgnoreCase) >= 0 ||
             path.IndexOf("/accounts/logout", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>The reason why the token must be refused (English, for the log; the caller never sees it), or null when it is good. No database access: unit tested.</summary>
        public static string Evaluate(Status status, string tokenStampClaim, string signingKey, string requestPath)
        {
            if (status == null || !status.IsActive || status.IsDeleted) return "The account is not active.";
            if (!TokenStamp.Matches(tokenStampClaim, status.SecurityStamp, signingKey)) return "The token was made before the last change of the account's password, roles or sessions.";
            if (status.MustChangePassword && !IsPasswordChangeOrLogout(requestPath)) return "The account must choose a new password first.";
            return null;
        }

        public static async Task ValidateAsync(TokenValidatedContext context)
        {
            if (!int.TryParse(context.Principal?.Identity?.GetUserId(), out int userId))
            {
                context.Fail("The token is not an access token.");
                return;
            }

            IUnitOfWork uow = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
            Status status = await uow.Set<User>().AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new Status
                {
                    IsActive = u.IsActive,
                    IsDeleted = u.IsDeleted,
                    SecurityStamp = u.SecurityStamp,
                    MustChangePassword = uow.Set<UserClaim>().Any(c => c.UserId == u.Id && c.ClaimType == IdentityRules.MustChangePasswordClaimType),
                })
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            string key = context.HttpContext.RequestServices.GetRequiredService<IOptionsSnapshot<BearerTokensOptions>>().Value.Key;
            string claim = context.Principal?.FindFirst(TokenStamp.ClaimType)?.Value;
            string problem = Evaluate(status, claim, key, context.HttpContext.Request.Path.Value);
            if (problem != null) context.Fail(problem);
        }
    }
}
