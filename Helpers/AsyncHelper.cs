using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyPlaylistCleaner_DotNET.Helpers
{
    internal static class AsyncHelper
    {
        /// <summary>
        /// Runs an async Task method synchronously
        /// </summary>
        /// <typeparam name="T">Return type of the async method</typeparam>
        /// <param name="task">Task to execute</param>
        /// <returns>Result of the task</returns>
        public static T RunSync<T>(Func<Task<T>> task)
        {
            var currentContext = SynchronizationContext.Current;
            var taskScheduler = TaskScheduler.Current;

            var newContext = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(newContext);

            try
            {
                var t = Task.Factory.StartNew(task,
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    taskScheduler).Unwrap().GetAwaiter();
                return t.GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(currentContext);
            }
        }

        /// <summary>
        /// Runs an async Task method synchronously
        /// </summary>
        /// <param name="task">Task to execute</param>
        public static void RunSync(Func<Task> task)
        {
            var currentContext = SynchronizationContext.Current;
            var taskScheduler = TaskScheduler.Current;

            var newContext = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(newContext);

            try
            {
                var t = Task.Factory.StartNew(task,
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    taskScheduler).Unwrap().GetAwaiter();
                t.GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(currentContext);
            }
        }

        /// <summary>
        /// Creates a cancellation token that will be canceled after the specified timeout
        /// </summary>
        /// <param name="timeout">The timeout period</param>
        /// <returns>A cancellation token</returns>
        public static CancellationToken CreateTimeoutToken(TimeSpan timeout)
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(timeout);
            return cts.Token;
        }

        /// <summary>
        /// Runs a task with a timeout
        /// </summary>
        /// <typeparam name="T">Type of the task result</typeparam>
        /// <param name="task">The task to run</param>
        /// <param name="timeout">The timeout period</param>
        /// <returns>The task result</returns>
        /// <exception cref="TimeoutException">Thrown if the task times out</exception>
        public static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
        {
            var timeoutTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(task, timeoutTask);

            if (completedTask == timeoutTask)
                throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");

            return await task; // Will re-throw any exception from the original task
        }

        /// <summary>
        /// Retries an operation a specified number of times with exponential backoff
        /// </summary>
        /// <typeparam name="T">Type of the operation result</typeparam>
        /// <param name="operation">The operation to retry</param>
        /// <param name="retryCount">Maximum number of retries</param>
        /// <param name="initialDelay">Initial delay between retries</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The operation result</returns>
        public static async Task<T> RetryWithBackoff<T>(
            Func<Task<T>> operation,
            int retryCount = 3,
            TimeSpan? initialDelay = null,
            CancellationToken cancellationToken = default)
        {
            var delay = initialDelay ?? TimeSpan.FromSeconds(1);

            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (i < retryCount - 1 &&
                                          !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    // Wait with exponential backoff before retrying
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromTicks(delay.Ticks * 2); // Exponential backoff
                }
            }

            // Last attempt, let any exception propagate
            return await operation();
        }
    }
}