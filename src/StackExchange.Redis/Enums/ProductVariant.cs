// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

/// <summary>
/// Indicates the flavor of RESP-server being used, if known. Unknown variants will be reported as <see cref="Redis"/>.
/// Inclusion (or omission) in this list does not imply support for any given variant; nor does it indicate any specific
/// relationship with the vendor or rights to use the name. It is provided solely for informational purposes. Identification
/// is not guaranteed, and is based on the server's self-reporting (typically via `INFO`), which may be incomplete or misleading.
/// </summary>
public enum ProductVariant
{
    /// <summary>
    /// The original Redis server. This is also the default value if the variant is unknown.
    /// </summary>
    Redis,

    /// <summary>
    /// <a href="https://valkey.io/">Valkey</a> is a fork of open-source Redis associated with AWS.
    /// </summary>
    Valkey,

    /// <summary>
    /// <a href="https://microsoft.github.io/garnet/">Garnet</a> is a Redis-compatible server from Microsoft.
    /// </summary>
    Garnet,

    // if you want to add another variant here, please open an issue with the details (variant name, INFO output, etc.)
}
