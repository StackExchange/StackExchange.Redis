using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis
{
    internal static class ExtensionMethodsInternal
    {
        internal static bool IsNullOrEmpty([NotNullWhen(false)] this string? s) =>
            string.IsNullOrEmpty(s);

        internal static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? s) =>
            string.IsNullOrWhiteSpace(s);
    }
}
