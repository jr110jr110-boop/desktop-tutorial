using System;
using System.Net;
using Common.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ViewModels.Exceptions;

namespace WebApi
{
    public class HttpGlobalExceptionFilter : IExceptionFilter
    {
        private readonly IWebHostEnvironment env;
        private readonly ILogger<HttpGlobalExceptionFilter> logger;

        public HttpGlobalExceptionFilter(IWebHostEnvironment env, ILogger<HttpGlobalExceptionFilter> logger)
        {
            this.env = env;
            this.logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            this.logger.LogError(new EventId(context.Exception.HResult),
                context.Exception,
                context.Exception.Message);

            if (context.Exception.GetType() == typeof(DomainInvalidException) ||
               context.Exception.GetType() == typeof(RepositoryInvalidException) ||
               context.Exception.GetType() == typeof(ServiceInvalidException) ||
               context.Exception.GetType() == typeof(DomainExistanceException) ||
               context.Exception.GetType() == typeof(RecordNotFoundException) ||
               context.Exception.GetType() == typeof(RecordExsistException))
            {
                var problemDetails = new ValidationProblemDetails()
                {
                    Instance = context.HttpContext.Request.Path,
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Please refer to the errors property for additional details."
                };

                problemDetails.Errors.Add("ErrorDetails", new string[] { context.Exception.Message.ToString() });

                context.Result = new JsonResult(ResponseDtoHelper.BadRequest<string>(context.Exception.Message.ToString()));
                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            }
            else
            {
                Exception exception = default(Exception);

                if (this.env.IsDevelopment())
                {
                    exception = context.Exception;
                }

                context.Result = new OkObjectResult(
                    ResponseDtoHelper.InternalServerErrosObject(context.Exception.Message, exception));
                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            context.ExceptionHandled = true;
        }

        private class JsonErrorResponse
        {
            public string[] Messages { get; set; }

            public object DeveloperMessage { get; set; }
        }
    }
}
