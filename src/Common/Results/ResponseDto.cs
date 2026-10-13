using System.Net;

namespace Common.Results
{
    public class ResponseDto<T>
    {
        public ResponseDto(bool success, string message = null, T data = default(T))
        {
            Success = success;
            Message = message;
            Data = data;
        }

        public bool Success { get; private set; }
        public string Message { get; private set; }
        public T Data { get; private set; }
        public HttpStatusCode HttpStatusCode { get; private set; }

        public ResponseDto<T> HasHttpStatusCode(HttpStatusCode httpStatusCode)
        {
            this.HttpStatusCode = httpStatusCode;

            return this;
        }
    }
}
