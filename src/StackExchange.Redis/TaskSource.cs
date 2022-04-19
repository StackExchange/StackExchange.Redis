using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal static class TaskSource
    {
        /// <summary>
        /// Create a new TaskCompletion source.
        /// </summary>
        /// <typeparam name="T">The type for the created <see cref="TaskCompletionSource{TResult}"/>.</typeparam>
        /// <param name="asyncState">The state for the created <see cref="TaskCompletionSource{TResult}"/>.</param>
        /// <param name="options">The options to apply to the task.</param>
        internal static TaskCompletionSource<T> Create<T>(object? asyncState, TaskCreationOptions options = TaskCreationOptions.None)
            => new TaskCompletionSource<T>(asyncState, options);
    }
}
