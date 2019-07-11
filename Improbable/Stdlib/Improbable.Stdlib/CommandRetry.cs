using System;
using System.Threading;
using System.Threading.Tasks;
using Improbable.Worker.CInterop;

namespace Improbable.Stdlib
{
    public static class CommandRetry
    {
        public static Task<TResult> Retry<TResult>(Func<Task<TResult>> action, int maxRetries = 10)
        {
            return Retry(action, CancellationToken.None, maxRetries, TimeSpan.FromSeconds(1));
        }

        public static Task<TResult> Retry<TResult>(Func<Task<TResult>> action, int maxRetries, TimeSpan delay)
        {
            return Retry(action, CancellationToken.None, maxRetries, delay);
        }

        public static Task<TResult> Retry<TResult>(Func<Task<TResult>> action, CancellationToken token, int maxRetries, TimeSpan delay)
        {
            return Task.Run(async () =>
            {
                var retriesLeft = maxRetries;

                while (retriesLeft > 0 && !token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await action();
                        return result;
                    }
                    catch (CommandFailedException e)
                    {
                        if (e.Code == StatusCode.AuthorityLost)
                        {
                            retriesLeft--;

                            if (retriesLeft > 0)
                            {
                                await Task.Delay(delay, token);
                            }
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                throw new CommandFailedException(StatusCode.Timeout, $"Giving up after {maxRetries} retries");
            }, token);
        }
    }
}
