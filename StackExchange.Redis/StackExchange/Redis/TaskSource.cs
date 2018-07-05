using System.Threading.Tasks;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace StackExchange.Redis
{
    internal static class TaskSource
    {
        /// <summary>
        /// Create a new TaskCompletion source
        /// </summary>
        /// <typeparam name="T">The type for the created <see cref="TaskCompletionSource{TResult}"/>.</typeparam>
        /// <param name="asyncState">The state for the created <see cref="TaskCompletionSource{TResult}"/>.</param>
        public static TaskCompletionSource<T> Create<T>(object asyncState)
            => new TaskCompletionSource<T>(asyncState, TaskCreationOptions.None);
    }
}
