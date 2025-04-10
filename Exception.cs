using System;

namespace SocketLibrary.Exceptions
{
    public class RetryFailedException : Exception
    {
        public RetryFailedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
