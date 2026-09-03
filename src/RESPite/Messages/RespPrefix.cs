namespace RESPite.Messages;

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
    /// Large integer number: (12...89\r\n.
    /// </summary>
    BigInteger = (byte)'(',

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

    /// <summary>
    /// Continuation of streaming scalar values.
    /// </summary>
    StreamContinuation = (byte)';',

    /// <summary>
    /// End sentinel for streaming aggregate values.
    /// </summary>
    StreamTerminator = (byte)'.',

    /// <summary>
    /// Metadata about the next element.
    /// </summary>
    Attribute = (byte)'|',
}
