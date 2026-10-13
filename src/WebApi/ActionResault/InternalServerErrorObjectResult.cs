namespace WebApi.ActionResault
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;

    public class InternalServerErrorObjectResult : ObjectResult
    {
        public InternalServerErrorObjectResult(object error)
            : base(error) => this.StatusCode = StatusCodes.Status500InternalServerError;
    }
}
