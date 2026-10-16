using System;
using RESPite;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

/// <summary>
/// Indicates the flavor of RESP-server being used, if known. Unknown variants will be reported as <see cref="Redis"/>.
/// Inclusion (or omission) in this list does not imply support for any given variant; nor does it indicate any specific
/// relationship with the vendor or rights to use the name. It is provided solely for informational purposes. Identification
/// is not guaranteed, and is based on the server's self-reporting (via `INFO` and `HELLO`), which may be incomplete or misleading.
/// </summary>
public enum ProductVariant
{
    /// <summary>
    /// The original Redis server. This is also the default value if the variant is unknown.
    /// </summary>
    [AsciiHash("redis")]
    Redis,

    /// <summary>
    /// <a href="https://valkey.io/">Valkey</a> is a fork of open-source Redis associated with AWS.
    /// </summary>
    [AsciiHash("valkey")]
    Valkey,

    /// <summary>
    /// <a href="https://microsoft.github.io/garnet/">Garnet</a> is a Redis-compatible server from Microsoft.
    /// </summary>
    [AsciiHash("garnet")]
    Garnet,

    /// <summary>
    /// <a href="https://www.dragonflydb.io/">Dragonfly</a>
    /// </summary>
    [AsciiHash("dragonfly")]
    Dragonfly,

    /// <summary>
    /// <a href="https://www.memurai.com/">Memurai</a>
    /// </summary>
    [AsciiHash("memurai")]
    Memurai,

    /// <summary>
    /// <a href="https://redict.io/">Redict</a>
    /// </summary>
    [AsciiHash("redict")]
    Redict,

    /// <summary>
    /// <a href="https://keydb.dev">KeyDB</a>
    /// </summary>
    [AsciiHash("keydb")]
    KeyDB,

    // if you want to add another variant here, please open an issue with the details (variant name, INFO output, etc.)
}

/// <summary>
/// Metadata and parsing methods for <see cref="ProductVariant"/>.
/// </summary>
internal static partial class ProductVariantMetadata
{
    /// <summary>
    /// Parses the <c>server</c> value reported by <c>HELLO</c> (<c>redis</c>, <c>valkey</c>, etc); this is the vendor's
    /// name rather than a protocol token, so the match is case-insensitive.
    /// </summary>
    [AsciiHash(CaseSensitive = false)]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out ProductVariant variant);
}
