using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Describes a single hash to be created during a bulk <see cref="IDatabase.HashImport"/> operation: a key plus the
/// field values for that key, supplied positionally against the shared field-name list of the import.
/// </summary>
/// <remarks>
/// The <see cref="Values"/> are matched positionally to the <c>fields</c> passed to the import, so
/// <see cref="ReadOnlyMemory{T}.Length"/> must equal the number of fields.
/// </remarks>
[Experimental(Experiments.Server_8_10, UrlFormat = Experiments.UrlFormat)]
public readonly struct HashImportEntry
{
    /// <summary>
    /// Initializes a <see cref="HashImportEntry"/> value.
    /// </summary>
    /// <param name="key">The key of the hash to create.</param>
    /// <param name="values">The field values, in the same order as the field names supplied to the import.</param>
    public HashImportEntry(RedisKey key, ReadOnlyMemory<RedisValue> values)
    {
        Key = key;
        Values = values;
    }

    /// <summary>
    /// The key of the hash to create.
    /// </summary>
    public RedisKey Key { get; }

    /// <summary>
    /// The field values for this hash, positionally matched to the shared field names of the import.
    /// </summary>
    public ReadOnlyMemory<RedisValue> Values { get; }
}
