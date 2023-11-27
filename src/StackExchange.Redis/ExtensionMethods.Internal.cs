using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis
{
    internal static class ExtensionMethodsInternal
    {
        internal static bool IsNullOrEmpty([NotNullWhen(false)] this string? s) =>
            string.IsNullOrEmpty(s);

        internal static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? s) =>
            string.IsNullOrWhiteSpace(s);

#if !NETCOREAPP3_1_OR_GREATER
        internal static bool TryDequeue<T>(this Queue<T> queue, [NotNullWhen(true)] out T? result)
        {
            if (queue.Count == 0)
            {
                result = default;
                return false;
            }
            result = queue.Dequeue()!;
            return true;
        }
        internal static bool TryPeek<T>(this Queue<T> queue, [NotNullWhen(true)] out T? result)
        {
            if (queue.Count == 0)
            {
                result = default;
                return false;
            }
            result = queue.Peek()!;
            return true;
        }
#endif
    }
}
