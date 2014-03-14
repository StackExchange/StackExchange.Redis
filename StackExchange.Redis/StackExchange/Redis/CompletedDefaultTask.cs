using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal static class CompletedTask<T>
    {
        private readonly static Task<T> @default = FromResult(default(T), null);

        public static Task<T> Default(object asyncState)
        {
            return asyncState == null ? @default : FromResult(default(T), asyncState);
        }
        public static Task<T> FromResult(T value, object asyncState)
        {
            var tcs = new TaskCompletionSource<T>(asyncState);
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
