using System;

namespace ViewModels.Exceptions
{
    public class RecordExsistException : Exception
    {
        public RecordExsistException()
        { }

        public RecordExsistException(string message)
            : base(message)
        { }

        public RecordExsistException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
