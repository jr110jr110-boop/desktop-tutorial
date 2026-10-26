using System.Linq;
using System.Threading.Tasks;
using Common.IdentityToolkit;
using DataLayer.Context;
using Entities.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Extensions
{
    /// <summary>
    /// Runs after a bearer token passed its signature, lifetime, issuer and audience checks (R1a).
    /// The token is valid only when it is an access token (it carries a user id; a refresh token does not) and the account still exists,
    /// is active and is not deleted. One small primary-key lookup per request; it closes the gap between "the user was deleted or
    /// deactivated" and "the access token expires".
    /// </summary>
    public static class JwtAccountStatus
    {
        public static async Task ValidateAsync(TokenValidatedContext context)
        {
            if (!int.TryParse(context.Principal?.Identity?.GetUserId(), out int userId))
            {
                context.Fail("The token is not an access token.");
                return;
            }

            IUnitOfWork uow = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
            var status = await uow.Set<User>().AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsActive, u.IsDeleted })
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            if (status == null || !status.IsActive || status.IsDeleted)
                context.Fail("The account is not active.");
        }
    }
}
