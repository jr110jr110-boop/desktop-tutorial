using Common.GuardToolkit;

namespace Common.Results
{
    public class Result<TData, TError>
    {
        public bool IsError { get; }
        public TData Data { get; }
        public TError Error { get; }

        public Result(TData data)
        {
            data.ThrowIfNull(nameof(data));
            IsError = false;
            Data = data;
        }

        public Result(TError error)
        {
            error.ThrowIfNull(nameof(error));
            IsError = true;
            Error = error;
        }
    }
}
