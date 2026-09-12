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
