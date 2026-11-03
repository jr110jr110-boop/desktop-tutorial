using System;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Finance;

namespace WebApi.Filters
{
    /// <summary>
    /// R1t D19: marks an action of a legacy Sepidar CSV channel. The channel works only when the setting <c>Sepidar:LegacyCsv</c> is on, and then only for the finance manager;
    /// everybody else (and everybody, while the setting is off) gets 403 with the standard refusal body. A use that went through is written to the audit trail.
    /// The role attribute of the action is the first line of defence (a PMO user or an accountant is turned away by it); this attribute holds the switch and the audit.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class LegacySepidarCsvAttribute : Attribute, IAsyncActionFilter
    {
        public LegacySepidarCsvAttribute(string channel) => Channel = channel;

        /// <summary>A short name of the channel for the audit trail and the logs.</summary>
        public string Channel { get; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            IServiceProvider services = context.HttpContext.RequestServices;
            bool manager = context.HttpContext.User?.IsInRole(SepidarLegacy.Roles) == true;
            string refusal = SepidarLegacy.Refusal(SepidarLegacy.Enabled(services.GetService<IConfiguration>()), manager);
            if (refusal != null)
            {
                context.Result = new ObjectResult(ResponseDtoHelper.Forbidden<object>(refusal)) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            ActionExecutedContext executed = await next();
            if (executed.Exception != null && !executed.ExceptionHandled) return;
            IUnitOfWork uow = services.GetService<IUnitOfWork>();
            if (uow == null) return;
            int.TryParse(context.HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int userId);
            SepidarLegacy.RecordUse(uow, userId, Channel);
            await uow.SaveChangesAsync();
        }
    }
}
