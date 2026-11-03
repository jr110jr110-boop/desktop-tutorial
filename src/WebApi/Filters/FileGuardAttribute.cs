using System;
using System.Reflection;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Files;
using ViewModels.NasbNiro.DocumentDtos;

namespace WebApi.Filters
{
    /// <summary>What the guarded action does with the file; it decides the question asked and the shape of a refusal.</summary>
    public enum FileGuardKind
    {
        /// <summary>The bytes of the file. A refusal is HTTP 403 with the standard refusal body; an unknown file is 404.</summary>
        Download = 0,

        /// <summary>Name, description, code and hash of the file. A refusal is the usual 200 with <c>success:false</c> (the action answers a DTO), so no name or description leaves.</summary>
        Info = 1,

        /// <summary>Replacing the bytes (answers a text).</summary>
        Replace = 2,

        /// <summary>Editing the name, description and code (answers a flag).</summary>
        Edit = 3,
    }

    /// <summary>
    /// R1t D8: runs the per-file access check before an action of the Documents controller. The check lives in <see cref="IFileAccessService"/>
    /// (the owning module's own read rule, the uploader rule for a loose file); this attribute only reads the file id from the action arguments and turns a refusal into the right answer.
    /// An id that is not a Guid, or a file that does not exist, goes on to the action, which already answers it. The authentication of the controller still runs first (an anonymous request is 401).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class FileGuardAttribute : Attribute, IAsyncActionFilter
    {
        public FileGuardAttribute(FileGuardKind kind, string argument)
        {
            Kind = kind;
            Argument = argument;
        }

        public FileGuardKind Kind { get; }

        /// <summary>The name of the action argument that carries the file id: a Guid, a text with a Guid, or an object with an <c>Id</c> property.</summary>
        public string Argument { get; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            Guid? id = IdOf(context);
            if (id == null)
            {
                await next();
                return;
            }

            IFileAccessService access = context.HttpContext.RequestServices.GetRequiredService<IFileAccessService>();
            bool read = Kind == FileGuardKind.Download || Kind == FileGuardKind.Info;
            FileVerdict verdict = read
                ? await access.ReadAsync(id.Value, context.HttpContext.RequestAborted)
                : await access.ChangeAsync(id.Value, context.HttpContext.RequestAborted);

            if (verdict.Kind == FileVerdictKind.Allowed)
            {
                await next();
                return;
            }

            if (verdict.Kind == FileVerdictKind.NotFound)
            {
                // the downloads answer 404 themselves; the other actions answer their own "not found" in their usual shape
                if (Kind == FileGuardKind.Download) context.Result = new NotFoundObjectResult(verdict.Message);
                else await next();
                return;
            }

            switch (Kind)
            {
                case FileGuardKind.Download:
                    // a letter's file answers like the letter's own endpoint: a stranger gets 404, so the two do not disagree about whether the file exists (L1)
                    if (verdict.LetterFile) { context.Result = new NotFoundObjectResult("فایلی یافت نشد"); break; }
                    context.Result = new ObjectResult(ResponseDtoHelper.Forbidden<string>(verdict.Message)) { StatusCode = StatusCodes.Status403Forbidden };
                    break;
                case FileGuardKind.Info:
                    context.Result = new OkObjectResult(ResponseDtoHelper.Forbidden<ResponseFileDto>(verdict.Message));
                    break;
                case FileGuardKind.Replace:
                    context.Result = new OkObjectResult(ResponseDtoHelper.Forbidden<string>(verdict.Message));
                    break;
                default:
                    context.Result = new OkObjectResult(ResponseDtoHelper.Forbidden<bool>(verdict.Message));
                    break;
            }
        }

        private Guid? IdOf(ActionExecutingContext context)
        {
            if (!context.ActionArguments.TryGetValue(Argument, out object value) || value == null) return null;
            if (value is Guid guid) return guid == Guid.Empty ? (Guid?)null : guid;
            if (value is string text) return Guid.TryParse(text, out Guid parsed) ? parsed : (Guid?)null;
            PropertyInfo property = value.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(value) is Guid fromObject && fromObject != Guid.Empty ? fromObject : (Guid?)null;
        }
    }
}
