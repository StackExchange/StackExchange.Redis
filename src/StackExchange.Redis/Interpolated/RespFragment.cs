using System;

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
    internal readonly ref struct RespFragment
    {
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
    internal sealed class RespAttribute : Attribute
    {
        public RespAttribute(params string[] tokens) => Tokens = tokens;

        public string[] Tokens { get; }
    }
}
