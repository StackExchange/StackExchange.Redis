using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Describes a single entry that failed during a bulk <see cref="IDatabase.HashImport"/> operation. The import is not
/// atomic, so individual entries can fail (for example, when a target key already holds a non-hash value) while others
/// succeed; <see cref="IDatabase.HashImport"/> returns the failures (an empty array indicates a fully successful import).
/// </summary>
[Experimental(Experiments.Server_8_10, UrlFormat = Experiments.UrlFormat)]
public readonly struct HashImportFailure
{
    internal HashImportFailure(int index, RedisKey key, string message)
    {
        Index = index;
        Key = key;
        Message = message;
    }

    /// <summary>
    /// The zero-based index of the failing entry within the entries supplied to the import.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// The key of the failing entry, in the caller's key-space (any keyspace-isolation prefix has been removed).
    /// </summary>
    public RedisKey Key { get; }

    /// <summary>
    /// The server error describing why the entry failed (for example, a <c>WRONGTYPE</c> message).
    /// </summary>
    public string Message { get; }
}
