using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Contains metadata information about an array returned by the ARINFO command.
/// </summary>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
public readonly struct ArrayInfo
{
    private readonly Dictionary<string, RedisValue>? otherValues;

    internal int OtherValueCount => otherValues?.Count ?? -1;

    /// <summary>
    /// Create a new instance with the specified values.
    /// </summary>
    public ArrayInfo(scoped ReadOnlySpan<KeyValuePair<string, RedisValue>> values)
    {
        foreach (ref readonly var pair in values)
        {
            if (string.IsNullOrEmpty(pair.Key)) continue;
            if (ArrayInfoFieldMetadata.TryParse(pair.Key, out var field))
            {
                switch (field)
                {
                    case ArrayInfoField.Count when TryRead(pair, out var index):
                        Count = index;
                        continue;
                    case ArrayInfoField.Length when TryRead(pair, out var index):
                        Length = index;
                        continue;
                    case ArrayInfoField.NextInsertIndex when TryRead(pair, out var index):
                        NextInsertIndex = index;
                        continue;
                    case ArrayInfoField.Slices when TryRead(pair, out var index):
                        Slices = index;
                        continue;
                    case ArrayInfoField.DirectorySize when TryRead(pair, out var index):
                        DirectorySize = index;
                        continue;
                    case ArrayInfoField.SuperDirEntries when TryRead(pair, out var index):
                        SuperDirEntries = index;
                        continue;
                    case ArrayInfoField.SliceSize when TryRead(pair, out var index):
                        SliceSize = index;
                        continue;
                }
            }
            // unknown field, or unable to handle directly
            otherValues ??= new();
            otherValues[pair.Key] = pair.Value;
        }
    }

    private static bool TryRead(in KeyValuePair<string, RedisValue> pair, out RedisArrayIndex value)
    {
        var val = pair.Value.Simplify();
        if (val.IsInteger)
        {
            try
            {
                value = new((ulong)val);
                return true;
            }
            catch (OverflowException) { }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Get values from the instance by key.
    /// </summary>
    public RedisValue this[string key]
    {
        get
        {
            if (!string.IsNullOrEmpty(key))
            {
                if (otherValues is { } && otherValues.TryGetValue(key, out var value))
                {
                    return value;
                }

                // spoof fields that are handled directly
                if (ArrayInfoFieldMetadata.TryParse(key, out var field))
                {
                    switch (field)
                    {
                        case ArrayInfoField.Count: return Count.ToRedisValue();
                        case ArrayInfoField.Length: return Length.ToRedisValue();
                        case ArrayInfoField.NextInsertIndex: return NextInsertIndex.ToRedisValue();
                        case ArrayInfoField.Slices: return Slices.ToRedisValue();
                        case ArrayInfoField.DirectorySize: return DirectorySize.ToRedisValue();
                        case ArrayInfoField.SuperDirEntries: return SuperDirEntries.ToRedisValue();
                        case ArrayInfoField.SliceSize: return SliceSize.ToRedisValue();
                    }
                }
            }
            return RedisValue.Null;
        }
    }

    /// <summary>
    /// Gets all array metadata values as a dictionary.
    /// </summary>
    public Dictionary<string, RedisValue> ToDictionary()
    {
        var result = otherValues is { }
            ? new Dictionary<string, RedisValue>(otherValues, otherValues.Comparer)
            : new Dictionary<string, RedisValue>();

        // for the *known* fields: they'll only be held in "otherValues" if we can't
        // parse the value naturally, so: the dictionary takes precedence, with the fields as fallback.
        AddIfMissing(result, ArrayInfoField.Count, Count);
        AddIfMissing(result, ArrayInfoField.Length, Length);
        AddIfMissing(result, ArrayInfoField.NextInsertIndex, NextInsertIndex);
        AddIfMissing(result, ArrayInfoField.Slices, Slices);
        AddIfMissing(result, ArrayInfoField.DirectorySize, DirectorySize);
        AddIfMissing(result, ArrayInfoField.SuperDirEntries, SuperDirEntries);
        AddIfMissing(result, ArrayInfoField.SliceSize, SliceSize);
        return result;

        static void AddIfMissing(Dictionary<string, RedisValue> values, ArrayInfoField field, RedisArrayIndex value)
        {
            if (ArrayInfoFieldMetadata.TryFormat(field, out var key) && !values.ContainsKey(key))
            {
                values.Add(key, value.ToRedisValue());
            }
        }
    }

    /// <summary>
    /// The number of array cells that have values.
    /// </summary>
    public RedisArrayIndex Count { get; }

    /// <summary>
    /// The notional length of the array.
    /// </summary>
    public RedisArrayIndex Length { get; }

    /// <summary>
    /// The current array write-head.
    /// </summary>
    public RedisArrayIndex NextInsertIndex { get; }

    /// <summary>
    /// The number of slices used by the array.
    /// </summary>
    public RedisArrayIndex Slices { get; }

    /// <summary>
    /// The size of the array directory.
    /// </summary>
    public RedisArrayIndex DirectorySize { get; }

    /// <summary>
    /// The number of super-directory entries.
    /// </summary>
    public RedisArrayIndex SuperDirEntries { get; }

    /// <summary>
    /// The configured slice size.
    /// </summary>
    public RedisArrayIndex SliceSize { get; }
}
