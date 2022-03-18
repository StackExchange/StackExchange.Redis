using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal static class CompletedTask<T>
    {
        private static readonly Task<T?> defaultTask = FromResult(default(T), null);

        public static Task<T?> Default(object? asyncState) => asyncState == null ? defaultTask : FromResult(default(T), asyncState);

        public static Task<T?> FromResult(T? value, object? asyncState)
        {
            if (asyncState == null) return Task.FromResult<T?>(value);
            // note we do not need to deny exec-sync here; the value will be known
            // before we hand it to them
            var tcs = TaskSource.Create<T?>(asyncState);
            tcs.SetResult(value);
            return tcs.Task;
        }

        public static Task<T> FromDefault(T value, object? asyncState)
        {
            if (asyncState == null) return Task.FromResult<T>(value);
            // note we do not need to deny exec-sync here; the value will be known
            // before we hand it to them
            var tcs = TaskSource.Create<T>(asyncState);
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
