using System;

namespace RESPite.Operations;

/// <summary>
/// What an operation wants done with its reply.
/// </summary>
/// <remarks>
/// These were marker interfaces on the parser object in the earlier spike. Here the message <i>is</i> the
/// parser, so there is nothing to type-test: the capability is stated once at construction and folded
/// into the message's flag word, which is where the hot path reads it from anyway.
/// </remarks>
[Flags]
internal enum RespParseOptions
{
    /// <summary>The reply is discarded and the result is <c>default</c>.</summary>
    Discard = 0,

    /// <summary>The reply is parsed, with the reader positioned on the value.</summary>
    Parse = 1 << 0,

    /// <summary>
    /// The reply is parsed from the outside, so the parser sees any attribute preceding the value.
    /// </summary>
    Metadata = (1 << 1) | Parse,

    /// <summary>
    /// Parsing is safe to run on the IO thread rather than handed to the pool.
    /// </summary>
    /// <remarks>
    /// Only for parsers that are cheap and cannot block or re-enter: the IO thread is not doing anything
    /// else while this runs, so an expensive parser here stalls every other reply on the connection.
    /// </remarks>
    Inline = (1 << 2) | Parse,
}
