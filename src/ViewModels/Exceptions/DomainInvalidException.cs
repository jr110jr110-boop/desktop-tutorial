
using System;

namespace ViewModels.Exceptions
{
    public class DomainInvalidException : Exception
    {
        public DomainInvalidException()
        { }

        public DomainInvalidException(string message)
            : base(message)
        { }

        public DomainInvalidException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
