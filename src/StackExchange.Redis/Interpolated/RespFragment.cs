using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. An already-framed run of one or more RESP bulk strings, written verbatim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Intended for fixed tokens — <c>EX</c>, <c>NX</c>, and the subcommand of a container command such as
    /// <c>CONFIG GET</c> or <c>CLIENT SETINFO LIB-NAME</c>. <see cref="ArgCount"/> is why this is a type
    /// rather than a bare <see cref="ReadOnlySpan{T}"/>: a fragment may be more than one argument, and
    /// without it the handler's argument count would silently disagree with the frame.
    /// </para>
    /// <para>
    /// These should be produced by a generator from a declared partial property rather than hand-written,
    /// so that framing, length prefixes and upper-casing are correct by construction; see
    /// <c>design/interpolated-resp-writer.md</c> section 2.3.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly ref struct RespFragment
    {
        /// <summary>
        /// Construct a fragment from raw bytes. Nothing validates them.
        /// </summary>
        /// <remarks>
        /// Deliberately gated: framing, length prefixes and <paramref name="argCount"/> must all be correct,
        /// and a mistake corrupts the connection for every command that follows, with the first symptom
        /// appearing somewhere unrelated. Declare a <c>[Resp]</c> partial property instead and let the
        /// generator emit all three correctly. Generated code suppresses this at the emit site, and nowhere
        /// wider.
        /// </remarks>
        // NOTE: no Message = here - ExperimentalAttribute.Message is .NET 9+, and this has to compile on
        // net461/netstandard2.0/net472/net8.0 too. SER011.md carries the explanation; UrlFormat links to it.
        [Experimental(Experiments.HandWrittenRespFragment, UrlFormat = Experiments.UrlFormat)]
        public RespFragment(ReadOnlySpan<byte> bytes, int argCount = 1)
        {
            if (argCount < 1) throw new ArgumentOutOfRangeException(nameof(argCount));
            Bytes = bytes;
            ArgCount = argCount;
        }

        /// <summary>
        /// This fragment when <paramref name="condition"/> holds, and nothing at all otherwise.
        /// </summary>
        /// <param name="condition">Whether the fragment should be written.</param>
        /// <remarks>
        /// <b>The spelling for an optional token.</b> A <c>default</c> fragment carries no bytes and no
        /// arguments, so an absent operand writes nothing and counts nothing - which is what an optional
        /// token has to do, since a <see cref="RedisValue"/> hole cannot express it (a null one writes an
        /// <i>empty</i> argument, and the server then reads a different command). This is the idiom that
        /// was already spelled <c>cond ? RespLiterals.X : default</c> in eight places, named.
        /// </remarks>
        public RespFragment When(bool condition) => condition ? this : default;

        /// <summary>
        /// This fragment when <paramref name="value"/> has one, and nothing at all otherwise.
        /// </summary>
        /// <typeparam name="T">The underlying value type.</typeparam>
        /// <param name="value">The value the token introduces.</param>
        /// <remarks>
        /// <b>For the <c>TOKEN n</c> shape</b>, where the token is present exactly when the value is:
        /// <code>
        /// $"{command}{key}{RespLiterals.Count.When(count)}{count}"
        /// </code>
        /// Taking the value itself rather than a <see cref="bool"/> is what keeps the two halves from
        /// disagreeing - both read the same <c>count</c>, so a present token with an absent value (which
        /// would be a malformed command, not merely a different one) takes two different variables to
        /// write, and is visible when it happens.
        /// </remarks>
        public RespFragment When<T>(T? value)
            where T : struct
            => value.HasValue ? this : default;

        /// <summary>
        /// Create a fragment from bytes, <b>checking</b> that they are well-formed RESP and that they contain
        /// exactly <paramref name="argCount"/> bulk strings.
        /// </summary>
        /// <param name="bytes">The candidate bytes.</param>
        /// <param name="argCount">How many bulk strings <paramref name="bytes"/> should contain.</param>
        /// <exception cref="ArgumentException">The bytes are not well-formed, or the count disagrees.</exception>
        /// <remarks>
        /// The sanctioned route for a fragment assembled at runtime — typically once, at startup, from
        /// configuration. Not gated, because the check is the point; the cost is irrelevant when it runs once,
        /// and it is the difference between a mistake that throws here and one that desyncs the connection
        /// somewhere unrelated. Prefer a generated <c>[Resp]</c> declaration whenever the tokens are known at
        /// compile time, which is almost always.
        /// </remarks>
        public static RespFragment CreateValidated(ReadOnlySpan<byte> bytes, int argCount = 1)
        {
            if (argCount < 1) throw new ArgumentOutOfRangeException(nameof(argCount));

            var found = 0;
            var offset = 0;
            while (offset < bytes.Length)
            {
                if (bytes[offset] != (byte)'$') throw Malformed($"expected '$' at offset {offset}");

                // length digits
                var start = ++offset;
                long length = 0;
                while (offset < bytes.Length && bytes[offset] >= (byte)'0' && bytes[offset] <= (byte)'9')
                {
                    length = (length * 10) + (bytes[offset] - (byte)'0');
                    if (length > int.MaxValue) throw Malformed($"length overflow at offset {start}");
                    offset++;
                }

                if (offset == start) throw Malformed($"missing length at offset {start}");
                if (offset + 1 >= bytes.Length || bytes[offset] != (byte)'\r' || bytes[offset + 1] != (byte)'\n')
                {
                    throw Malformed($"expected CRLF after the length at offset {offset}");
                }

                offset += 2;
                if (offset + length + 2 > bytes.Length) throw Malformed($"payload of {length} runs past the end");

                offset += (int)length;
                if (bytes[offset] != (byte)'\r' || bytes[offset + 1] != (byte)'\n')
                {
                    throw Malformed($"expected CRLF after the payload at offset {offset}");
                }

                offset += 2;
                found++;
            }

            if (found != argCount) throw Malformed($"contains {found} bulk string(s), but {argCount} was declared");

            return new RespFragment(bytes, argCount);
        }

        private static ArgumentException Malformed(string detail)
            => new($"Not a well-formed run of RESP bulk strings: {detail}.", "bytes");

        /// <summary>The pre-framed bytes, including every <c>$len</c> prefix and trailing CRLF.</summary>
        public ReadOnlySpan<byte> Bytes { get; }

        /// <summary>How many RESP arguments <see cref="Bytes"/> contains.</summary>
        public int ArgCount { get; }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. Declares the tokens a generated <see cref="RespFragment"/> property should emit.
    /// Omit the tokens to infer a single token from the member name, as <c>AsciiHashAttribute</c> does.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public sealed class RespAttribute : Attribute
    {
        /// <summary>Infer a single token from the member name, upper-cased.</summary>
        public RespAttribute() => Tokens = Array.Empty<string>();

        /// <summary>Use these tokens verbatim; casing is not adjusted.</summary>
        public RespAttribute(string token) => Tokens = new[] { token };

        /// <summary>Use these tokens verbatim, as a multi-argument fragment; casing is not adjusted.</summary>
        public RespAttribute(string token, params string[] additionalTokens)
        {
            var tokens = new string[additionalTokens.Length + 1];
            tokens[0] = token;
            additionalTokens.CopyTo(tokens, 1);
            Tokens = tokens;
        }

        /// <summary>The tokens to emit; empty means "infer from the member name".</summary>
        public string[] Tokens { get; }
    }
}
