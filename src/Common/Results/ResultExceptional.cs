using System;

namespace Common.Results
{
    public class ResultExceptional<TData> : Result<TData, Exception>
    {
        public ResultExceptional(TData data) : base(data)
        {
        }

        public ResultExceptional(Exception error) : base(error)
        {
        }
    }
}
