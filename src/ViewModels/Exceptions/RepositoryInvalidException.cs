using System;

namespace ViewModels.Exceptions
{
    public class RepositoryInvalidException : Exception
    {
        public RepositoryInvalidException()
        { }

        public RepositoryInvalidException(string message)
            : base(message)
        { }

        public RepositoryInvalidException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}

