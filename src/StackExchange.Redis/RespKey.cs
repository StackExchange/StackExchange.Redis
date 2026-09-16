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
    // The bytes to write: raw UTF-8 for a Blob, or UTF-16 code units reinterpreted for a Clob.
    //
    // ONE field for three input shapes. ReadOnlyMemory<byte> collapses into the span case, because the
    // only reason to keep a Memory distinct from its Span is to outlive the call and nothing here does;
    // a char span reinterprets via MemoryMarshal, which halves the struct against the obvious
    // two-spans-and-a-discriminator layout.
    private readonly ReadOnlySpan<byte> _payload;

    // THREE states, not a bool, so that this can express everything RedisKey can.
    //
    // A bool would have forced null to be refused or silently flattened to the empty key - and "" is
    // shared, legal, and almost never what a null variable meant. It also makes default(RespKey) agree
    // with default(RedisKey): both are null, where a bool discriminator would have made the default an
    // empty Blob instead.
    //
    // Emptiness cannot carry this: an empty key is legal in Redis, so "no bytes" cannot mean "no key".
    private readonly Kind _kind;

    private enum Kind : byte
    {
        /// <summary>No key at all - what <c>default(RespKey)</c> and a null string are.</summary>
        Null = 0,

        /// <summary>Bytes, already UTF-8.</summary>
        Blob = 1,

        /// <summary>Characters, encoded to UTF-8 on the way out.</summary>
        Clob = 2,
    }

    /// <summary>A key from UTF-8 bytes the caller already holds.</summary>
    /// <param name="value">The key's bytes; borrowed for the duration of the write.</param>
    public RespKey(ReadOnlySpan<byte> value)
    {
        _payload = value;
        _kind = Kind.Blob;
    }

    /// <summary>A key from characters, encoded as UTF-8 when written.</summary>
    /// <param name="value">The key's text; borrowed for the duration of the write.</param>
    public RespKey(ReadOnlySpan<char> value)
    {
        _payload = MemoryMarshal.AsBytes(value);
        _kind = Kind.Clob;
    }

    /// <summary>A key from a string.</summary>
    /// <param name="value">The key's text; borrowed for the duration of the write.</param>
    /// <remarks>
    /// <para>
    /// <b>Not redundant with the <see cref="ReadOnlySpan{T}"/> overload</b>, because the implicit
    /// conversion from <see cref="string"/> to <c>ReadOnlySpan&lt;char&gt;</c> only exists from
    /// netstandard2.1 onwards. Without this, <c>new RespKey("k")</c> would compile on the newer targets
    /// and fail on net461/net472/netstandard2.0 - which is the worst shape a gap can take, since it
    /// builds for whoever wrote it and breaks for whoever consumes it.
    /// </para>
    /// <para>
    /// <b>A null string is a null key</b>, not the empty one. This type has a state for it, so it does
    /// not have to choose between refusing null and silently writing to <c>""</c> - a key that is shared,
    /// legal, and almost never what a null variable meant.
    /// </para>
    /// </remarks>
    public RespKey(string? value)
    {
        if (value is null)
        {
            _payload = default;
            _kind = Kind.Null;
        }
        else
        {
            _payload = MemoryMarshal.AsBytes(value.AsSpan());
            _kind = Kind.Clob;
        }
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

    /// <summary>Whether this is the absence of a key, rather than an empty one.</summary>
    /// <remarks>
    /// <para>
    /// <b>Expressing null is this type's job; writing one is not.</b> This is a key, not a request slot -
    /// so it represents what a caller has, and the writer decides what can go on the wire. Today a RESP
    /// request is an array of bulk strings with no null among them, so a null key renders as empty,
    /// exactly as <see cref="RedisKey"/> does. That is a property of RESP2/3 request framing rather than
    /// of this type, and is not assumed permanent - typed or nullable arguments would change what the
    /// writer does without changing what a key is.
    /// </para>
    /// </remarks>
    public bool IsNull => _kind == Kind.Null;

    /// <summary>How many bytes this key occupies on the wire, before any context prefix.</summary>
    /// <remarks>
    /// The span overload of <see cref="Encoding.GetByteCount(char*, int)"/> only exists on the newer
    /// targets - unlike <c>GetBytes</c>, which <c>System.Memory</c> supplies everywhere - so down-level
    /// takes the pointer form rather than allocating a <c>char[]</c> to ask the question.
    /// </remarks>
    internal int GetByteCount()
    {
        if (_kind != Kind.Clob) return _payload.Length;

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
        if (_kind != Kind.Clob)
        {
            _payload.CopyTo(target);
            return _payload.Length;
        }

        return Encoding.UTF8.GetBytes(Chars, target);
    }
}
