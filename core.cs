using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using SocketLibrary.Exceptions;

namespace SocketLibrary.Core
{
    public class RetryPolicy
    {
        private readonly int _maxRetries;
        private readonly TimeSpan _delay;

        public RetryPolicy(int maxRetries = 3, int delayMilliseconds = 1000)
        {
            _maxRetries = maxRetries;
            _delay = TimeSpan.FromMilliseconds(delayMilliseconds);
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
        {
            int attempts = 0;
            Exception lastException = null;

            while (attempts < _maxRetries)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (IsTransientException(ex))
                {
                    lastException = ex;
                    attempts++;

                    if (attempts >= _maxRetries)
                        break;

                    await Task.Delay(_delay);
                }
            }

            throw new RetryFailedException($"Operation failed after {_maxRetries} attempts", lastException);
        }

        public async Task ExecuteAsync(Func<Task> operation)
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true;
            });
        }

        private bool IsTransientException(Exception ex)
        {
            return ex is SocketException ||
                   ex is IOException ||
                   ex is TimeoutException;
        }
    }
}
