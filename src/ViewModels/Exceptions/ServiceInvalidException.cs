using System;

namespace ViewModels.Exceptions
{
    public class ServiceInvalidException : Exception
    {
        public ServiceInvalidException()
        { }

        public ServiceInvalidException(string message)
            : base(message)
        { }

        public ServiceInvalidException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}

