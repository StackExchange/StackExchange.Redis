namespace StackExchange.Redis;

/// <summary>
/// Indicates the protocol for communicating with the server.
/// </summary>
public enum RedisProtocol
{
    // note: the non-binary safe protocol is not supported by the client, although the parser does support it (it is used in the toy server)

    // important: please use "major_minor_revision" numbers (two digit minor/revision), to allow for possible scenarios like
    // "hey, we've added RESP 3.1; oops, we've added RESP 3.1.1"

    /// <summary>
    /// The protocol used by all redis server versions since 1.2, as defined by https://github.com/redis/redis-specifications/blob/master/protocol/RESP2.md
    /// </summary>
    Resp2 = 2_00_00, // major__minor__revision
    /// <summary>
    /// Opt-in variant introduced in server version 6, as defined by https://github.com/redis/redis-specifications/blob/master/protocol/RESP3.md
    /// </summary>
    Resp3 = 3_00_00, // major__minor__revision
}
