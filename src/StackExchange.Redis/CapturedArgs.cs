using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StackExchange.Redis;

/// <summary>
/// Helpers for the captured-argument structs emitted by <c>[AutoDatabase(Replays = true)]</c>.
/// </summary>
/// <remarks>
/// A database that replays re-invokes with the same captured arguments after the caller has regained
/// control, and a <see cref="Memory{T}"/> argument still belongs to that caller - who is entitled to reuse
/// the buffer once the call returns. So a replaying capture takes a shallow copy and gives it back when the
/// operation is finally done with.
/// <para>
/// Shallow: the elements are copied, not anything they refer to. A <see cref="RedisValue"/> wrapping a
/// caller's <c>Memory&lt;byte&gt;</c> still points at the caller's bytes, so mutating *those* would still
/// be seen by a replay. That gap closes when serialization moves to the calling thread; this covers the
/// buffer the caller is most likely to reuse.
/// </para>
/// </remarks>
internal static class CapturedArgs
{
    /// <summary>Take a pooled shallow copy of a captured argument.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="value">The caller's memory to copy.</param>
    /// <returns>A copy owned by us, to be given back via <see cref="Release{T}(in ReadOnlyMemory{T})"/>.</returns>
    public static ReadOnlyMemory<T> Clone<T>(ReadOnlyMemory<T> value)
    {
        if (value.IsEmpty) return default; // nothing rented, so nothing for Release to give back

        var rented = ArrayPool<T>.Shared.Rent(value.Length);
        value.Span.CopyTo(rented);
        return new ReadOnlyMemory<T>(rented, 0, value.Length);
    }

    /// <inheritdoc cref="Clone{T}(ReadOnlyMemory{T})"/>
    public static Memory<T> Clone<T>(Memory<T> value)
    {
        if (value.IsEmpty) return default;

        var rented = ArrayPool<T>.Shared.Rent(value.Length);
        value.Span.CopyTo(rented);
        return new Memory<T>(rented, 0, value.Length);
    }

    /// <summary>Give a cloned argument back to the pool.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="field">The captured field, cleared as it is released.</param>
    /// <remarks>
    /// Clears the field first, so a second call has nothing to give back. Returning the same array twice is
    /// not an error the pool reports - it simply pools it twice, and two later rents then hand out the same
    /// array to different callers - so this is worth the cheek of writing through a readonly field.
    /// <para>
    /// Sequential only: the clear is not atomic with the read. That is sufficient here because a captured
    /// state is a local within a single async flow, never shared. Note also that calling this on a
    /// defensive copy - i.e. via a <c>readonly</c> holder - would clear the copy and leak; the generated
    /// funnels hold their state in a plain local.
    /// </para>
    /// </remarks>
    public static void Release<T>(in ReadOnlyMemory<T> field)
    {
        var captured = field;
        Unsafe.AsRef(in field) = default;
        Return(captured);
    }

    /// <inheritdoc cref="Release{T}(in ReadOnlyMemory{T})"/>
    public static void Release<T>(in Memory<T> field)
    {
        var captured = field;
        Unsafe.AsRef(in field) = default;
        Return<T>(captured);
    }

    private static void Return<T>(ReadOnlyMemory<T> captured)
    {
        // note the Length check rather than a null check: an empty ReadOnlyMemory reports a non-null,
        // zero-length array through TryGetArray, so testing for null here would never be true
        if (MemoryMarshal.TryGetArray(captured, out var segment) && segment.Array is { Length: > 0 })
        {
            // cleared on return: these elements can hold references (RedisValue, RedisKeyOrValue), and a
            // pooled array would otherwise keep the caller's keys and values alive indefinitely
            ArrayPool<T>.Shared.Return(segment.Array, clearArray: true);
        }
    }
}
