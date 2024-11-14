using RESPite.Resp.Commands;
using static RESPite.Resp.Client.CommandFactory;

namespace RESPite.Resp.KeyValueStore;

/// <summary>
/// Keyspace commands.
/// </summary>
public static class Keys
{
    /// <summary>
    /// Iterates keys in a database.
    /// </summary>
    public static readonly RespCommand<Empty, long> DBSIZE = new(Default);

    /// <summary>
    /// Iterates keys in a database.
    /// </summary>
    public static readonly RespCommand<Scan.Request, Scan.Response> SCAN = new(Default);

    /// <summary>
    /// Gets the type of the requested key.
    /// </summary>
    public static readonly RespCommand<SimpleString, KnownType> TYPE = new(Default);

    /// <summary>
    /// Database storage type.
    /// </summary>
    public enum KnownType : byte
    {
        /// <summary>
        /// An unknown or unrecognized value.
        /// </summary>
        Unknown,

        /// <summary>
        /// No value.
        /// </summary>
        None,

        /// <summary>
        /// Strings.
        /// </summary>
        String,

        /// <summary>
        /// Lists.
        /// </summary>
        List,

        /// <summary>
        /// Sets.
        /// </summary>
        Set,

        /// <summary>
        /// Sorted sets.
        /// </summary>
        ZSet,

        /// <summary>
        /// Hashes (maps).
        /// </summary>
        Hash,

        /// <summary>
        /// Streams.
        /// </summary>
        Stream,
    }
}
