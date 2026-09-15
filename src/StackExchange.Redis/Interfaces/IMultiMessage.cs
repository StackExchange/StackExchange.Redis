using System.Collections.Generic;

namespace StackExchange.Redis
{
    /// <summary>
    /// A message that may expand into several messages written as one unit, so nothing interleaves between
    /// them and all of them reach one connection.
    /// </summary>
    internal interface IMultiMessage
    {
        /// <summary>
        /// The messages to write, or <c>null</c> to decline expanding and be written as an ordinary
        /// single message.
        /// </summary>
        /// <param name="connection">The connection these will be written to.</param>
        /// <remarks>
        /// <para>
        /// <b>Declining is the common case for some of these, not an edge case.</b> An <c>EVALSHA</c> pairs
        /// itself with a <c>SCRIPT LOAD</c> only while the endpoint is not believed to hold the script;
        /// once it is, the pair degenerates to "just me", and returning <c>null</c> says so without
        /// building an enumerator to carry a single element back to a caller that has a better path for it.
        /// </para>
        /// <para>
        /// <b>The decision and the expansion arrive together deliberately.</b> Both are a function of
        /// <paramref name="connection"/> - "can this connection use read-only scripts", "does this endpoint
        /// hold this script" - and a message is re-written to a <i>different</i> connection on
        /// <c>MOVED</c>, on reconnect, and when the backlog drains. Splitting this into "ask, then fetch"
        /// would invite the answer to be cached between the two calls, and a cached answer is exactly what
        /// those retries invalidate.
        /// </para>
        /// <para>
        /// Note that implementations written as C# iterators run no body code until the first
        /// <c>MoveNext</c>, so anything needed to <i>decide</i> must live in a non-iterator wrapper around
        /// the iterator rather than inside it. That wrapper runs before the connection's database is
        /// selected, so a decision may not depend on the selected database.
        /// </para>
        /// </remarks>
        IEnumerable<Message>? GetMessages(PhysicalConnection connection);
    }
}
