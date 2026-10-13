using System.Net;

namespace Common.Results
{
    public static class ResponseDtoHelper
    {
        public static ResponseDto<T> Create<T>(bool success, string message = null, T data = default(T))
        {
            return new ResponseDto<T>(success, message, data);
        }

        public static ResponseDto<T> Ok<T>(T data, string message = null)
        {
            return Create(true, message, data)
                .HasHttpStatusCode(HttpStatusCode.OK);
        }

        public static ResponseDto<T> BadRequest<T>(string message)
        {
            return Create(false, message, default(T))
                .HasHttpStatusCode(HttpStatusCode.BadRequest);
        }

        public static ResponseDto<object> InternalServerErrosObject(string message, object @object)
        {
            return Create(false, message, @object)
                .HasHttpStatusCode(HttpStatusCode.InternalServerError);
        }

        public static ResponseDto<T> NotFound<T>(string message)
        {
            return Create(false, message, default(T))
                .HasHttpStatusCode(HttpStatusCode.NotFound);
        }

        public static ResponseDto<T> Unauthorized<T>(string message)
        {
            return Create(false, message, default(T))
                .HasHttpStatusCode(HttpStatusCode.Unauthorized);
        }
    }
}
