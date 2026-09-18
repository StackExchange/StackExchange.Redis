using StackExchange.Redis.Protocol;
namespace StackExchange.Redis
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

        /// <summary>The <c>LIMIT</c> operand; a count follows it.</summary>
        [Resp]
        internal static partial RespFragment Limit { get; }

        /// <summary>The <c>APPROX</c> operand of the set-cardinality commands.</summary>
        [Resp]
        internal static partial RespFragment Approx { get; }

        /// <summary>The <c>WITHSCORES</c> operand of the sorted-set reads.</summary>
        [Resp]
        internal static partial RespFragment WithScores { get; }

        /// <summary>The <c>CH</c> operand of <c>ZADD</c>: count changed members, not just new ones.</summary>
        [Resp]
        internal static partial RespFragment Ch { get; }

        /// <summary>The <c>INCR</c> operand of <c>ZADD</c>.</summary>
        [Resp]
        internal static partial RespFragment Incr { get; }

        /// <summary>The <c>REV</c> operand of <c>ZRANGE</c>/<c>ZRANGESTORE</c>.</summary>
        [Resp]
        internal static partial RespFragment Rev { get; }

        /// <summary>The <c>WEIGHTS</c> operand of the sorted-set combinations; one weight per key follows.</summary>
        [Resp]
        internal static partial RespFragment Weights { get; }

        /// <summary>
        /// The <c>AGGREGATE</c> operand of the sorted-set combinations; a mode follows it.
        /// </summary>
        /// <remarks>
        /// Separate from the mode rather than one two-token fragment, because an attribute taking an array
        /// is not CLS-compliant and this assembly is. <c>RespAggregate</c> writes the pair.
        /// </remarks>
        [Resp]
        internal static partial RespFragment Aggregate { get; }

        /// <summary>The <c>MIN</c> end, as <c>ZMPOP</c> names it.</summary>
        [Resp]
        internal static partial RespFragment Min { get; }

        /// <inheritdoc cref="Min"/>
        [Resp]
        internal static partial RespFragment Max { get; }

        /// <summary>The <c>BYSCORE</c> operand of <c>ZRANGESTORE</c>.</summary>
        [Resp]
        internal static partial RespFragment ByScore { get; }

        /// <summary>The <c>BYLEX</c> operand of <c>ZRANGESTORE</c>.</summary>
        [Resp]
        internal static partial RespFragment ByLex { get; }

        /// <summary>The <c>COUNT</c> operand of <c>ZMPOP</c>, and the aggregation mode of the same name.</summary>
        [Resp]
        internal static partial RespFragment Count { get; }

        /// <summary>The <c>LEFT</c> end, as the list commands name it.</summary>
        [Resp]
        internal static partial RespFragment Left { get; }

        /// <inheritdoc cref="Left"/>
        [Resp]
        internal static partial RespFragment Right { get; }

        /// <summary>The <c>BEFORE</c> position of <c>LINSERT</c>.</summary>
        [Resp]
        internal static partial RespFragment Before { get; }

        /// <inheritdoc cref="Before"/>
        [Resp]
        internal static partial RespFragment After { get; }

        /// <summary>The <c>RANK</c> operand of <c>LPOS</c>; a rank follows it.</summary>
        [Resp]
        internal static partial RespFragment Rank { get; }

        /// <summary>The <c>MAXLEN</c> operand of <c>LPOS</c>; a length follows it.</summary>
        [Resp]
        internal static partial RespFragment MaxLen { get; }

        /// <summary>The <c>BULK</c> ordering of <c>LMOVEM</c>.</summary>
        [Resp]
        internal static partial RespFragment Bulk { get; }

        /// <summary>The <c>OBO</c> (one-by-one) ordering of <c>LMOVEM</c>.</summary>
        [Resp]
        internal static partial RespFragment Obo { get; }

        /// <summary>The <c>EXACTLY</c> count mode of <c>LMOVEM</c>.</summary>
        [Resp]
        internal static partial RespFragment Exactly { get; }

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

        /// <summary>The <c>DB</c> operand of <c>COPY</c>.</summary>
        [Resp]
        internal static partial RespFragment Db { get; }

        /// <summary>The <c>REPLACE</c> operand of <c>COPY</c>.</summary>
        [Resp]
        internal static partial RespFragment Replace { get; }

        /// <summary>The <c>LOAD</c> subcommand of <c>SCRIPT</c>.</summary>
        [Resp]
        internal static partial RespFragment Load { get; }

        /// <summary>The <c>BY</c> operand of <c>SORT</c>; a pattern follows it.</summary>
        [Resp]
        internal static partial RespFragment By { get; }

        /// <summary>The <c>DESC</c> operand of <c>SORT</c>.</summary>
        [Resp]
        internal static partial RespFragment Desc { get; }

        /// <summary>The <c>ALPHA</c> operand of <c>SORT</c>.</summary>
        [Resp]
        internal static partial RespFragment Alpha { get; }

        /// <summary>The <c>STORE</c> operand of <c>SORT</c>; a destination key follows it.</summary>
        [Resp]
        internal static partial RespFragment Store { get; }

        /// <summary>The <c>ASC</c> ordering.</summary>
        [Resp]
        internal static partial RespFragment Asc { get; }

        /// <summary>The <c>FROMMEMBER</c> origin of <c>GEOSEARCH</c>; a member follows it.</summary>
        [Resp]
        internal static partial RespFragment FromMember { get; }

        /// <summary>The <c>FROMLONLAT</c> origin of <c>GEOSEARCH</c>; two coordinates follow it.</summary>
        [Resp]
        internal static partial RespFragment FromLonLat { get; }

        /// <summary>The <c>BYRADIUS</c> shape; a radius and a unit follow it.</summary>
        [Resp]
        internal static partial RespFragment ByRadius { get; }

        /// <summary>The <c>BYBOX</c> shape; a width, a height and a unit follow it.</summary>
        [Resp]
        internal static partial RespFragment ByBox { get; }

        /// <summary>The <c>ANY</c> operand of <c>GEOSEARCH</c>: stop as soon as COUNT is satisfied.</summary>
        [Resp]
        internal static partial RespFragment Any { get; }

        /// <summary>The <c>WITHCOORD</c> operand of the geo queries.</summary>
        [Resp]
        internal static partial RespFragment WithCoord { get; }

        /// <summary>The <c>WITHDIST</c> operand of the geo queries.</summary>
        [Resp]
        internal static partial RespFragment WithDist { get; }

        /// <summary>The <c>WITHHASH</c> operand of the geo queries.</summary>
        [Resp]
        internal static partial RespFragment WithHash { get; }

        /// <summary>The <c>STOREDIST</c> operand of <c>GEOSEARCHSTORE</c>.</summary>
        [Resp]
        internal static partial RespFragment StoreDist { get; }

        /// <summary>The <c>REDUCE</c> operand of <c>VADD</c>; a dimension follows it.</summary>
        [Resp]
        internal static partial RespFragment Reduce { get; }

        /// <summary>The <c>FP32</c> vector encoding; one bulk string of raw floats follows it.</summary>
        [Resp]
        internal static partial RespFragment Fp32 { get; }

        /// <summary>The <c>VALUES</c> vector encoding; a count and that many components follow it.</summary>
        [Resp]
        internal static partial RespFragment Values { get; }

        /// <summary>The <c>CAS</c> operand of <c>VADD</c>: check and set.</summary>
        [Resp]
        internal static partial RespFragment Cas { get; }

        /// <summary>The <c>NOQUANT</c> operand of <c>VADD</c>: store the vectors as given.</summary>
        [Resp]
        internal static partial RespFragment NoQuant { get; }

        /// <summary>The <c>BIN</c> operand of <c>VADD</c>: quantize to one bit per component.</summary>
        [Resp]
        internal static partial RespFragment Bin { get; }

        /// <summary>The <c>EF</c> operand of the vector-set commands; an effort follows it.</summary>
        [Resp]
        internal static partial RespFragment Ef { get; }

        /// <summary>The <c>SETATTR</c> operand of <c>VADD</c>; a JSON document follows it.</summary>
        [Resp]
        internal static partial RespFragment SetAttr { get; }

        /// <summary>The <c>M</c> operand of <c>VADD</c>; a link count follows it.</summary>
        [Resp]
        internal static partial RespFragment M { get; }

        /// <summary>The <c>ELE</c> origin of <c>VSIM</c>; a member follows it.</summary>
        [Resp]
        internal static partial RespFragment Ele { get; }

        /// <summary>The <c>WITHATTRIBS</c> operand of <c>VSIM</c>.</summary>
        [Resp]
        internal static partial RespFragment WithAttribs { get; }

        /// <summary>The <c>EPSILON</c> operand of <c>VSIM</c>; a distance follows it.</summary>
        [Resp]
        internal static partial RespFragment Epsilon { get; }

        /// <summary>The <c>FILTER</c> operand of <c>VSIM</c>; an expression follows it.</summary>
        [Resp]
        internal static partial RespFragment Filter { get; }

        /// <summary>The <c>FILTER-EF</c> operand of <c>VSIM</c>; an effort follows it.</summary>
        /// <remarks>Spelled out, because the member name cannot carry the hyphen.</remarks>
        [Resp("FILTER-EF")]
        internal static partial RespFragment FilterEf { get; }

        /// <summary>The <c>FULL</c> operand of <c>ARINFO</c>.</summary>
        [Resp]
        internal static partial RespFragment Full { get; }

        /// <summary>The <c>SUM</c> operation of <c>AROP</c>.</summary>
        [Resp]
        internal static partial RespFragment Sum { get; }

        /// <summary>The <c>MATCH</c> operation of <c>AROP</c>.</summary>
        [Resp]
        internal static partial RespFragment Match { get; }

        /// <summary>The <c>USED</c> operation of <c>AROP</c>.</summary>
        [Resp]
        internal static partial RespFragment Used { get; }

        /// <summary>The <c>MINID</c> trim strategy of <c>XTRIM</c>.</summary>
        [Resp]
        internal static partial RespFragment MinId { get; }

        /// <summary>The <c>~</c> marker that makes a stream trim approximate.</summary>
        [Resp("~")]
        internal static partial RespFragment Approximate { get; }

        /// <summary>The <c>CREATE</c> subcommand of <c>XGROUP</c>.</summary>
        [Resp]
        internal static partial RespFragment Create { get; }

        /// <summary>The <c>DESTROY</c> subcommand of <c>XGROUP</c>.</summary>
        [Resp]
        internal static partial RespFragment Destroy { get; }

        /// <summary>The <c>SETID</c> subcommand of <c>XGROUP</c>.</summary>
        [Resp("SETID")]
        internal static partial RespFragment SetId { get; }

        /// <summary>The <c>DELCONSUMER</c> subcommand of <c>XGROUP</c>.</summary>
        [Resp("DELCONSUMER")]
        internal static partial RespFragment DeleteConsumer { get; }

        /// <summary>The <c>MKSTREAM</c> operand of <c>XGROUP CREATE</c>.</summary>
        [Resp("MKSTREAM")]
        internal static partial RespFragment MkStream { get; }

        /// <summary>The <c>IDS</c> operand of <c>XDELEX</c>/<c>XACKDEL</c>; a count follows it.</summary>
        [Resp]
        internal static partial RespFragment Ids { get; }

        /// <summary>The <c>KEEPREF</c> trim mode: entries are removed, references kept.</summary>
        [Resp("KEEPREF")]
        internal static partial RespFragment KeepRef { get; }

        /// <summary>The <c>DELREF</c> trim mode.</summary>
        [Resp("DELREF")]
        internal static partial RespFragment DelRef { get; }

        /// <summary>The <c>ACKED</c> trim mode.</summary>
        [Resp]
        internal static partial RespFragment Acked { get; }

        /// <summary>The <c>JUSTID</c> operand of <c>XCLAIM</c>/<c>XAUTOCLAIM</c>: ids without fields.</summary>
        [Resp("JUSTID")]
        internal static partial RespFragment JustId { get; }

        /// <summary>The <c>NOMKSTREAM</c> operand of <c>XADD</c>.</summary>
        [Resp("NOMKSTREAM")]
        internal static partial RespFragment NoMkStream { get; }

        /// <summary>The <c>IDMP</c> operand of <c>XADD</c>; a producer id and an entry id follow it.</summary>
        [Resp]
        internal static partial RespFragment Idmp { get; }

        /// <summary>The <c>IDMPAUTO</c> operand of <c>XADD</c>; a producer id follows it.</summary>
        [Resp("IDMPAUTO")]
        internal static partial RespFragment IdmpAuto { get; }

        /// <summary>The <c>SILENT</c> mode of <c>XNACK</c>: released without counting a failure.</summary>
        [Resp]
        internal static partial RespFragment Silent { get; }

        /// <summary>The <c>FAIL</c> mode of <c>XNACK</c>: released and counted as a failed delivery.</summary>
        [Resp]
        internal static partial RespFragment Fail { get; }

        /// <summary>The <c>FATAL</c> mode of <c>XNACK</c>: released and marked a terminal failure.</summary>
        [Resp]
        internal static partial RespFragment Fatal { get; }

        /// <summary>The <c>IDMP-DURATION</c> operand of <c>XCFGSET</c>.</summary>
        [Resp("IDMP-DURATION")]
        internal static partial RespFragment IdmpDuration { get; }

        /// <summary>The <c>IDMP-MAXSIZE</c> operand of <c>XCFGSET</c>.</summary>
        [Resp("IDMP-MAXSIZE")]
        internal static partial RespFragment IdmpMaxSize { get; }

        /// <summary>The <c>ENCODING</c> subcommand of <c>OBJECT</c>.</summary>
        [Resp]
        internal static partial RespFragment Encoding { get; }

        /// <summary>The <c>FREQ</c> subcommand of <c>OBJECT</c>.</summary>
        [Resp]
        internal static partial RespFragment Freq { get; }

        /// <summary>The <c>IDLETIME</c> subcommand of <c>OBJECT</c>.</summary>
        [Resp]
        internal static partial RespFragment IdleTime { get; }

        /// <summary>The <c>REFCOUNT</c> subcommand of <c>OBJECT</c>.</summary>
        [Resp]
        internal static partial RespFragment RefCount { get; }

        /// <summary>The <c>TRUTH</c> operand of <c>VSIM</c>: exact search rather than approximate.</summary>
        [Resp]
        internal static partial RespFragment Truth { get; }

        /// <summary>The <c>NOTHREAD</c> operand of <c>VSIM</c>.</summary>
        [Resp]
        internal static partial RespFragment NoThread { get; }
    }
}
