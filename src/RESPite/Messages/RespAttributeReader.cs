using System.Diagnostics.CodeAnalysis;

namespace RESPite.Messages;

/// <summary>
/// Allows attribute data to be parsed conveniently.
/// </summary>
/// <typeparam name="T">The type of data represented by this reader.</typeparam>
[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public abstract class RespAttributeReader<T>
{
    /// <summary>
    /// Parse a group of attributes.
    /// </summary>
    public virtual void Read(ref RespReader reader, ref T value)
    {
        reader.Demand(RespPrefix.Attribute);
        _ = ReadKeyValuePairs(ref reader, ref value);
    }

    /// <summary>
    /// Parse an aggregate as a set of key/value pairs.
    /// </summary>
    /// <returns>The number of pairs successfully processed.</returns>
    protected virtual int ReadKeyValuePairs(ref RespReader reader, ref T value)
    {
        var iterator = reader.AggregateChildren();

        byte[] pooledBuffer = [];
        Span<byte> localBuffer = stackalloc byte[128];
        int count = 0;
        while (iterator.MoveNext() && iterator.Value.TryReadNext())
        {
            if (iterator.Value.IsScalar)
            {
                var key = iterator.Value.Buffer(ref pooledBuffer, localBuffer);

                if (iterator.MoveNext() && iterator.Value.TryReadNext())
                {
                    if (ReadKeyValuePair(key, ref iterator.Value, ref value))
                    {
                        count++;
                    }
                }
                else
                {
                    break; // no matching value for this key
                }
            }
            else
            {
                if (iterator.MoveNext() && iterator.Value.TryReadNext())
                {
                    // we won't try to handle aggregate keys; skip the value
                }
                else
                {
                    break; // no matching value for this key
                }
            }
        }
        iterator.MovePast(out reader);
        return count;
    }

    /// <summary>
    /// Parse an individual key/value pair.
    /// </summary>
    /// <returns>True if the pair was successfully processed.</returns>
    public virtual bool ReadKeyValuePair(scoped ReadOnlySpan<byte> key, ref RespReader reader, ref T value) => false;
}
