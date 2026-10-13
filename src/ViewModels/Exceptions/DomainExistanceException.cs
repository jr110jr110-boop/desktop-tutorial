using System;

namespace ViewModels.Exceptions
{
    public class DomainExistanceException : Exception
    {
        public DomainExistanceException()
        { }

        public DomainExistanceException(string message)
            : base(message)
        { }

        public DomainExistanceException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
