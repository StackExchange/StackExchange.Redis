using System;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Commands;

/// <summary>
/// Keyspace commands.
/// </summary>
public static class Keys
{
    /// <summary>
    /// Iterates keys in a database.
    /// </summary>
    public static readonly RespCommand<Empty, long> DBSIZE = new(PinnedPrefixWriter.None("*1\r\n$6\r\nDBSIZE\r\n"u8), RespReaders.Int64);

    /// <summary>
    /// Iterates keys in a database.
    /// </summary>
    public static readonly RespCommand<Scan, Scan.Response> SCAN = new(Scan.ScanWriter.Instance, Scan.ScanReader.Instance);

    /// <summary>
    /// Gets the type of the requested key.
    /// </summary>
    public static readonly RespCommand<ReadOnlyMemory<byte>, KnownType> TYPE = new(PinnedPrefixWriter.Memory("*2\r\n$4\r\nTYPE\r\n"u8), RespReaders.EnumReader<KnownType>.Instance);

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
