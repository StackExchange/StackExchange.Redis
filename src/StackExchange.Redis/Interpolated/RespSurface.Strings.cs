using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The string-command group: <c>target.Strings.Set(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain wrapper over one <see cref="RespContext"/> field - deliberately NOT a reinterpret-cast of a
    /// layout-compatible struct. Holding exactly one field of that type makes the layout identical <i>by
    /// construction</i>, so the wrapper IS the pun, enforced by the compiler and with no <c>Unsafe</c>. A
    /// by-value pun would copy the same bytes anyway; only a <c>ref</c> pun avoids the copy, and that
    /// requires a stable address, which drags <c>ref readonly</c> and its lifetime rules into every caller
    /// to save a few register moves ahead of a network round trip.
    /// </para>
    /// <para>
    /// Not a <c>ref struct</c>, for the same reason <see cref="RespContext"/> is not: these have to survive
    /// an <c>await</c>.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespStrings
    {
        private readonly RespContext _context;

        /// <summary>Group the string commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespStrings(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespTarget target)
        {
            /// <summary>The string commands.</summary>
            public RespStrings Strings => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The string commands.</summary>
            public RespStrings Strings => new(context);
        }

        // The group ACCESSORS above have to be extension blocks - an extension property has no other
        // spelling. The COMMANDS below are deliberately ordinary `this` extension methods, and that is
        // not nostalgia: it is what makes retiring one cheap later. Decommissioning an obsolete command
        // is then a matter of deleting the `this` - new call sites bind to whatever replaced it, while
        // already-compiled callers keep working, because the static method they were compiled against is
        // still there, same name, same signature, same assembly. No MissingMethodException, no major
        // version. An extension block member cannot be retired that gently. See AGENTS.md, "Backwards
        // compatibility is paramount".
        //
        // `in` because RespStrings is a readonly struct: no defensive copy, and nothing to copy on the
        // way to a network round trip.

        /// <summary>GET.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> Get(this in RespStrings strings, RedisKey key, CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<RedisValue>(
                $"{RedisCommand.GET}{key}", flags.WithRetryCategory(CommandFlags.CommandRetryReadOnly));

        /// <summary>SET, in full: expiration and value condition included.</summary>
        /// <param name="strings">The string command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="expiry">When the key should expire; default for no expiration.</param>
        /// <param name="when">The condition the write is subject to; default to write unconditionally.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// Deliberately the <b>most complicated</b> command in the spike, because it is the one that tests
        /// the design rather than demonstrating it: between them <paramref name="expiry"/> and
        /// <paramref name="when"/> render anywhere from zero to five extra arguments, so the command's
        /// arity is not known until run time.
        /// </para>
        /// <para>
        /// It is still one straight line of writing. The legacy builder
        /// (<c>RedisDatabase.GetStringSetMessage</c>) is a ~17-branch decision tree, and most of those
        /// branches are not about Redis at all - they pick between fixed-arity <c>Message.Create</c>
        /// overloads, one branch per token count, which is a cost the interpolated form simply does not
        /// have.
        /// </para>
        /// <para>
        /// <b>Condition before expiration</b>, which is the documented grammar:
        /// <c>SET key value [NX|XX|IFEQ cmp] [GET] [EX s|PX ms|EXAT|PXAT|KEEPTTL]</c>. Redis itself parses
        /// the tail as an order-insensitive loop - which is how the legacy builder gets away with emitting
        /// <c>EX n XX</c> - but other RESP servers need not be as forgiving, and matching the documentation
        /// costs nothing.
        /// </para>
        /// <para>
        /// <b>It emits the canonical <c>SET</c> and nothing else</b>, where the legacy builder also reaches
        /// for <c>SETNX</c>, <c>SETEX</c> and <c>PSETEX</c>. <c>SETEX</c>/<c>PSETEX</c> are pure arity
        /// relics - identical semantics and reply to <c>SET ... EX n</c>. <c>SETNX</c> is <b>not</b>: it
        /// replies <c>:1</c>/<c>:0</c> where <c>SET ... NX</c> replies <c>+OK</c>/nil, so collapsing it here
        /// is a real divergence from the old surface, taken deliberately - <c>SET ... NX</c> has been
        /// available since 2.6.12 and one reply shape beats two.
        /// </para>
        /// <para>
        /// <b>One expression, including the optional arguments.</b> Compose/Complete is not needed here -
        /// that pairing exists for a fragment whose <i>presence</i> is a branch in the caller's logic, and
        /// nothing here branches. The result stays <c>ValueTask&lt;bool&gt;</c> rather than the result-less
        /// <c>SendAsync</c>, because under NX/XX/IFEQ the boolean is real information: a nil reply means
        /// the write did not happen, which is not an error.
        /// </para>
        /// <para>
        /// The retry category comes from the condition: a conditional write is checked, an unconditional
        /// one is last-wins. <see cref="ValueCondition.RetryCategory"/> returns
        /// <see cref="CommandFlags.None"/> for "no opinion", and <c>WithRetryCategory</c> is first-wins, so
        /// a caller who names a category still keeps it.
        /// </para>
        /// </remarks>
        public static ValueTask<bool> Set(
            this in RespStrings strings,
            RedisKey key,
            RedisValue value,
            Expiration expiry = default,
            ValueCondition when = default,
            CommandFlags flags = CommandFlags.None)
            => strings.Context.SendAsync<bool>(
                $"{RedisCommand.SET}{key}{value}{when}{expiry}",
                flags.WithRetryCategory(when.RetryCategory)
                     .WithRetryCategory(CommandFlags.CommandRetryWriteLastWins));
    }
}
