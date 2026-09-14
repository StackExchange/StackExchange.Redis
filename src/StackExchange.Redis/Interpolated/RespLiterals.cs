namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The keyword arguments the command surface writes, pre-framed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The author writes the declaration; <c>RespFragmentGenerator</c> emits the bytes, the length
    /// prefixes and the argument count, so all three are correct by construction rather than by review.
    /// Hand-written fragments are gated behind SER011 precisely because a mistake in any of them desyncs
    /// the connection, with the first symptom appearing somewhere unrelated.
    /// </para>
    /// <para>
    /// <b>Distinct from <see cref="RedisLiterals"/></b>, which holds the same tokens as
    /// <see cref="RedisValue"/>s for the <c>MessageWriter</c> path: those are framed at write time, these
    /// are framed at compile time. Neither is a wrapper for the other, and the pair is temporary - the
    /// older one retires with the writer it serves.
    /// </para>
    /// <para>
    /// Only tokens that cannot come from somewhere better live here. A keyword that belongs to a value -
    /// <c>EX</c>/<c>PXAT</c>/<c>KEEPTTL</c> to <see cref="Expiration"/>, <c>NX</c>/<c>IFEQ</c> to
    /// <see cref="ValueCondition"/> - stays owned by that type, which is what lets
    /// <c>$"{cmd}{key}{value}{when}{expiry}"</c> render the whole of SET without a branch.
    /// </para>
    /// </remarks>
    internal static partial class RespLiterals
    {
        /// <summary>The <c>GET</c> operand of <c>SET</c>, which makes it reply with the previous value.</summary>
        [Resp]
        internal static partial RespFragment Get { get; }

        /// <summary>The <c>LEN</c> operand of <c>LCS</c>.</summary>
        [Resp]
        internal static partial RespFragment Len { get; }

        /// <summary>The <c>IDX</c> operand of <c>LCS</c>.</summary>
        [Resp]
        internal static partial RespFragment Idx { get; }

        /// <summary>The <c>MINMATCHLEN</c> operand of <c>LCS</c>; a length follows it.</summary>
        [Resp]
        internal static partial RespFragment MinMatchLen { get; }

        /// <summary>The <c>WITHMATCHLEN</c> operand of <c>LCS</c>.</summary>
        [Resp]
        internal static partial RespFragment WithMatchLen { get; }

        /// <summary>
        /// The <c>BIT</c> index type of <c>BITCOUNT</c>/<c>BITPOS</c>. There is deliberately no
        /// <c>BYTE</c> counterpart: it is the server's own default, so it is rendered as nothing at all.
        /// </summary>
        [Resp]
        internal static partial RespFragment Bit { get; }

        /// <summary>The <c>BYINT</c> increment kind of <c>INCREX</c>; the amount follows it.</summary>
        [Resp]
        internal static partial RespFragment ByInt { get; }

        /// <summary>The <c>BYFLOAT</c> increment kind of <c>INCREX</c>; the amount follows it.</summary>
        [Resp]
        internal static partial RespFragment ByFloat { get; }

        /// <summary>The <c>LBOUND</c> operand of <c>INCREX</c>; the bound follows it.</summary>
        [Resp]
        internal static partial RespFragment LBound { get; }

        /// <summary>The <c>UBOUND</c> operand of <c>INCREX</c>; the bound follows it.</summary>
        [Resp]
        internal static partial RespFragment UBound { get; }

        /// <summary>The <c>SATURATE</c> operand of <c>INCREX</c>.</summary>
        [Resp]
        internal static partial RespFragment Saturate { get; }

        /// <summary>The <c>AND</c> operation of <c>BITOP</c>.</summary>
        [Resp]
        internal static partial RespFragment And { get; }

        /// <summary>The <c>OR</c> operation of <c>BITOP</c>.</summary>
        [Resp]
        internal static partial RespFragment Or { get; }

        /// <summary>The <c>XOR</c> operation of <c>BITOP</c>.</summary>
        [Resp]
        internal static partial RespFragment Xor { get; }

        /// <summary>The <c>NOT</c> operation of <c>BITOP</c>.</summary>
        [Resp]
        internal static partial RespFragment Not { get; }

        /// <summary>The <c>DIFF</c> operation of <c>BITOP</c>.</summary>
        [Resp]
        internal static partial RespFragment Diff { get; }

        /// <summary>The <c>DIFF1</c> operation of <c>BITOP</c>.</summary>
        [Resp]
        internal static partial RespFragment Diff1 { get; }

        /// <summary>The <c>ANDOR</c> operation of <c>BITOP</c>.</summary>
        [Resp]
        internal static partial RespFragment AndOr { get; }

        /// <summary>The <c>ONE</c> operation of <c>BITOP</c>.</summary>
        [Resp]
        internal static partial RespFragment One { get; }

        /// <summary>
        /// The <c>FIELDS</c> keyword of the hash field-lifetime commands; a count and that many field
        /// names follow it.
        /// </summary>
        [Resp]
        internal static partial RespFragment Fields { get; }

        /// <summary>The <c>WITHVALUES</c> operand of <c>HRANDFIELD</c>.</summary>
        [Resp]
        internal static partial RespFragment WithValues { get; }

        /// <summary>
        /// The <c>FNX</c> field condition of <c>HSETEX</c>. Note it is <b>not</b> <c>NX</c>: the key-level
        /// spelling that <see cref="ValueCondition"/> writes means something else here, so this is a
        /// separate token rather than a reuse.
        /// </summary>
        [Resp]
        internal static partial RespFragment Fnx { get; }

        /// <inheritdoc cref="Fnx"/>
        [Resp]
        internal static partial RespFragment Fxx { get; }

        /// <summary>The <c>NX</c> condition of the hash field-expiry commands.</summary>
        [Resp]
        internal static partial RespFragment Nx { get; }

        /// <inheritdoc cref="Nx"/>
        [Resp]
        internal static partial RespFragment Xx { get; }

        /// <inheritdoc cref="Nx"/>
        [Resp]
        internal static partial RespFragment Gt { get; }

        /// <inheritdoc cref="Nx"/>
        [Resp]
        internal static partial RespFragment Lt { get; }
    }
}
