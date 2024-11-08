namespace RESPite.Resp;

/// <summary>
/// RESP protocol prefix.
/// </summary>
public enum RespPrefix : byte
{
    /// <summary>
    /// Invalid.
    /// </summary>
    None = 0,

    /// <summary>
    /// Simple strings: +OK\r\n.
    /// </summary>
    SimpleString = (byte)'+',

    /// <summary>
    /// Simple errors: -ERR message\r\n.
    /// </summary>
    SimpleError = (byte)'-',

    /// <summary>
    /// Integers: :123\r\n.
    /// </summary>
    Integer = (byte)':',

    /// <summary>
    /// String with support for binary data: $7\r\nmessage\r\n.
    /// </summary>
    BulkString = (byte)'$',

    /// <summary>
    /// Multiple inner messages: *1\r\n+message\r\n.
    /// </summary>
    Array = (byte)'*',

    /// <summary>
    /// Null strings/arrays: _\r\n.
    /// </summary>
    Null = (byte)'_',

    /// <summary>
    /// Boolean values: #T\r\n.
    /// </summary>
    Boolean = (byte)'#',

    /// <summary>
    /// Floating-point number: ,123.45\r\n.
    /// </summary>
    Double = (byte)',',

    /// <summary>
    /// Large floating-point number: (123.45\r\n.
    /// </summary>
    BigNumber = (byte)'(',

    /// <summary>
    /// Error with support for binary data: !7\r\nmessage\r\n.
    /// </summary>
    BulkError = (byte)'!',

    /// <summary>
    /// String that should be interpreted verbatim: =11\r\ntxt:message\r\n.
    /// </summary>
    VerbatimString = (byte)'=',

    /// <summary>
    /// Multiple sub-items that represent a map.
    /// </summary>
    Map = (byte)'%',

    /// <summary>
    /// Multiple sub-items that represent a set.
    /// </summary>
    Set = (byte)'~',

    /// <summary>
    /// Out-of band messages.
    /// </summary>
    Push = (byte)'>',

    // these are not actually implemented by any server; no
    // longer part of RESP3?
    // Stream = (byte)';',
    // UnboundEnd = (byte)'.',
    // Attribute = (byte)'|',
}
