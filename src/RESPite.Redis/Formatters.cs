namespace RESPite.Redis;

internal static class Formatters
{
    private const string Global = "global::RESPite.Redis";

    public const string KeyStringArray =
        $"{Global}.{nameof(KeyStringArrayFormatter)}.{nameof(KeyStringArrayFormatter.Instance)}";
}
