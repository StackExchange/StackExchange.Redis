using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. A key the writer <b>borrows</b>, rather than one it owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>The borrowed counterpart to <see cref="RedisKey"/></b>, exactly as <see cref="RESPite.Messages.RespValue"/>
/// is to <see cref="RedisValue"/> - owned versus borrowed is the distinction the <c>Resp</c> prefix
/// already carries in this codebase.
/// </para>
/// <para>
/// <b>Why this can hold a span when <see cref="RedisKey"/> cannot.</b> A <see cref="RedisKey"/> stores
/// its bytes, so it can only accept something with a lifetime - a <see cref="string"/> or an array. The
/// interpolated writer copies each hole into its rented buffer <i>before</i> <c>AppendFormatted</c>
/// returns, so a key it is handed never has to outlive the call. That is the whole of the lifetime
/// objection to spans-as-keys, and it simply does not arise here.
/// </para>
/// <para>
/// <b>Why it must be said explicitly.</b> A key is not a value: it takes the context's key prefix, is
/// marked for client-side invalidation, and folds into the cluster slot. Nothing about a
/// <see cref="ReadOnlySpan{T}"/> of bytes says which it is, and guessing wrongly is silent - the frame
/// stays well-formed and the server accepts it, while key-prefix isolation is broken and the command
/// routes to the wrong node. So the role is stated by naming this type.
/// </para>
/// <para>
/// <b>Constructors rather than <c>AsKey()</c> extension methods</b>, deliberately: an extension on
/// <see cref="string"/> and <see cref="ReadOnlySpan{T}"/> would offer itself on every string and span in
/// any file that imports this namespace, which is nearly all of them. The cost is four characters at the
/// call site; the benefit is that nothing is added to a type everybody already uses.
/// </para>
/// </remarks>
[Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
public readonly ref struct RespKey
{
    // UTF-8 bytes ready to write, OR UTF-16 code units reinterpreted as bytes; _isUtf16 says which.
    //
    // ONE field for three input shapes. ReadOnlyMemory<byte> collapses into the byte case, because the
    // only reason to keep a Memory distinct from its Span is to outlive the call - and nothing here
    // does. A char span reinterprets via MemoryMarshal, which halves the struct against the obvious
    // two-spans-and-a-discriminator layout.
    private readonly ReadOnlySpan<byte> _payload;

    // NOT inferred from emptiness: an empty key is legal in Redis, so "no chars" cannot mean "not
    // chars". That would work in every test until somebody stored under "".
    private readonly bool _isUtf16;

    /// <summary>A key from UTF-8 bytes the caller already holds.</summary>
    /// <param name="value">The key's bytes; borrowed for the duration of the write.</param>
    public RespKey(ReadOnlySpan<byte> value)
    {
        _payload = value;
        _isUtf16 = false;
    }

    /// <summary>A key from characters, encoded as UTF-8 when written.</summary>
    /// <param name="value">The key's text; borrowed for the duration of the write.</param>
    public RespKey(ReadOnlySpan<char> value)
    {
        _payload = MemoryMarshal.AsBytes(value);
        _isUtf16 = true;
    }

    /// <summary>A key from UTF-8 bytes held in memory.</summary>
    /// <param name="value">The key's bytes; only the span is taken, since nothing outlives the write.</param>
    public RespKey(ReadOnlyMemory<byte> value)
        : this(value.Span)
    {
    }

    /// <summary>The characters, when this key was built from them.</summary>
    /// <remarks>
    /// Safe by construction: <c>_isUtf16</c> is set only from a real <see cref="char"/> span, so the
    /// byte length is necessarily even and the cast cannot lose a trailing byte.
    /// </remarks>
    private ReadOnlySpan<char> Chars => MemoryMarshal.Cast<byte, char>(_payload);

    /// <summary>How many bytes this key occupies on the wire, before any context prefix.</summary>
    /// <remarks>
    /// The span overload of <see cref="Encoding.GetByteCount(char*, int)"/> only exists on the newer
    /// targets - unlike <c>GetBytes</c>, which <c>System.Memory</c> supplies everywhere - so down-level
    /// takes the pointer form rather than allocating a <c>char[]</c> to ask the question.
    /// </remarks>
    internal int GetByteCount()
    {
        if (!_isUtf16) return _payload.Length;

        var chars = Chars;
        if (chars.IsEmpty) return 0;
#if NET6_0_OR_GREATER
        return Encoding.UTF8.GetByteCount(chars);
#else
        unsafe
        {
            fixed (char* ptr = chars)
            {
                return Encoding.UTF8.GetByteCount(ptr, chars.Length);
            }
        }
#endif
    }

    /// <summary>Write this key's bytes, encoding if it was built from characters.</summary>
    /// <param name="target">Where to write; must be at least <see cref="GetByteCount"/> long.</param>
    /// <returns>How many bytes were written.</returns>
    internal int CopyTo(Span<byte> target)
    {
        if (!_isUtf16)
        {
            _payload.CopyTo(target);
            return _payload.Length;
        }

        return Encoding.UTF8.GetBytes(Chars, target);
    }
}
