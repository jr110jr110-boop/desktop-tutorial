using System.Threading;
using System.Threading.Tasks;

namespace Common.Extentions
{
    public static class TaskExtensions
    {
        public static Task<T> WithCancellationTokenAsync<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            return Task.Run(async () => await task, cancellationToken);
        }

        public static Task WithCancellationTokenAsync(this Task task, CancellationToken cancellationToken)
        {
            return Task.Run(async () => await task, cancellationToken);
        }

        public static Task<T> WithCancellationTokenAsync<T>(this ValueTask<T> task, CancellationToken cancellationToken)
        {
            return Task.Run(async () => await task, cancellationToken);
        }

        public static Task WithCancellationTokenAsync(this ValueTask task, CancellationToken cancellationToken)
        {
            return Task.Run(async () => await task, cancellationToken);
        }
    }
}
