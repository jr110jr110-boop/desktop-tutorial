namespace Common.Results
{
    public static class ResultExtensions
    {
        public static bool IsSuccess<TData, TError>(this Result<TData, TError> result)
            => !result.IsError;

        public static TData GetData<TData, TError>(this Result<TData, TError> result)
            => result.Data;

        public static TError GetError<TData, TError>(this Result<TData, TError> result)
           => result.Error;
    }
}
