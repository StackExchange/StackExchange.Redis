using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Sentinel address field names.
/// </summary>
internal enum SentinelAddressField
{
    /// <summary>
    /// Unknown or unrecognized field.
    /// </summary>
    [AsciiHash("")]
    Unknown = 0,

    /// <summary>
    /// IP address field.
    /// </summary>
    [AsciiHash("ip")]
    Ip,

    /// <summary>
    /// Port field.
    /// </summary>
    [AsciiHash("port")]
    Port,
}

/// <summary>
/// Metadata for SentinelAddressField enum parsing.
/// </summary>
internal static partial class SentinelAddressFieldMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out SentinelAddressField field);
}
