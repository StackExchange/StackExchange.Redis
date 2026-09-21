using System.Threading;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// The <c>SELECT n</c> frames, rendered once per database and shared by every sender.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Memoised because a congested, interleaved workload issues a great many of these.</b> Two
    /// databases in use on one connection means every alternation costs a <c>SELECT</c>, so the rendering
    /// is on the hot path in exactly the case this exists to support - and it is the same eleven bytes
    /// every time.
    /// </para>
    /// <para>
    /// Sharing one rendering between concurrent senders is sound because these are <b>detached over a
    /// fixed buffer that is never returned to a pool</b>: releasing one is a no-op rather than a hand-back,
    /// which is precisely the case the second <c>SendWithPreambleAsync</c> overload documents itself as
    /// existing for. A pooled frame could not be shared - the first release would put the array back while
    /// another sender was still writing from it.
    /// </para>
    /// <para>
    /// Keyed by command map as well as by index, because <c>SELECT</c> can be renamed or disabled like any
    /// other command, and a table shared across multiplexers would hand one deployment's spelling to
    /// another.
    /// </para>
    /// </remarks>
    internal sealed class SelectPreamble
    {
        // -1 is the "no database" sentinel the frame uses, so the table starts at 0 and an index outside
        // it simply renders on demand rather than growing an unbounded array for a hostile value
        private const int Memoised = 16;

        private readonly RespContext _context;

        // holders rather than the structs themselves: a RespRequest is several fields wide, so storing one
        // directly would be a torn read away from a request pointing at the wrong length. The reference is
        // the thing that has to be atomic, and a reference is.
        private readonly Holder?[] _cache = new Holder?[Memoised];

        internal SelectPreamble(RespContext context) => _context = context;

        /// <summary>The <c>SELECT</c> for this database, rendered once where it is worth remembering.</summary>
        /// <param name="database">The database index.</param>
        internal RespRequest For(int database)
        {
            if ((uint)database >= Memoised) return Render(database);

            // a benign race renders twice and keeps one; both are valid and neither owns poolable memory,
            // so the loser is simply garbage rather than a buffer that has to be returned
            if (Volatile.Read(ref _cache[database]) is { } cached) return cached.Request;

            var rendered = Render(database);
            Volatile.Write(ref _cache[database], new Holder(rendered));
            return rendered;
        }

        private sealed class Holder(RespRequest request)
        {
            internal readonly RespRequest Request = request;
        }

        private RespRequest Render(int database)
        {
            var frame = _context.Render($"{RedisCommand.SELECT}{(RedisValue)database}");
            try
            {
                // COPIED out of the pooled frame into a right-sized array of its own, rather than detached.
                // Detaching would adopt the pooled buffer, and the first sender to release it would hand it
                // back while another was still writing from the same bytes. Owning nothing poolable is what
                // makes this shareable, and it is why the release at the end of a send is a no-op.
                var bytes = frame.AsLookupKey().Span.ToArray();

                // CommandRetryAlways matches what the pair already stamps on a head: re-selecting is
                // idempotent, and a retry that skipped it would run against the wrong database
                return new RespRequest(
                    bytes,
                    lease: null,
                    offset: 0,
                    length: bytes.Length,
                    keyMarks: 0,
                    slot: ServerSelectionStrategy.NoSlot,
                    argCount: frame.ArgCount,
                    flags: CommandFlags.CommandRetryAlways,
                    command: RedisCommand.SELECT,
                    database: database);
            }
            finally
            {
                frame.Dispose();
            }
        }
    }
}
