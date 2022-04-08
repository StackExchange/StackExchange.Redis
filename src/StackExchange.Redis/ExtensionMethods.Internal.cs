using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis
{
    internal static class ExtensionMethodsInternal
    {
        public static bool IsNullOrEmpty([NotNullWhen(false)] this string? s) =>
            string.IsNullOrEmpty(s);

        public static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? s) =>
            string.IsNullOrWhiteSpace(s);
    }
}
