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

        /// <summary>
        /// Whether writing this message <b>without</b> its expansion is still correct.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Inside a transaction the expansion is never asked for: <c>QueuedMessage</c> wraps each inner
        /// operation and is not itself an <see cref="IMultiMessage"/>, so only <c>WriteImpl</c> runs. For
        /// most of these that silently drops something load-bearing, which is why each such type carries a
        /// hand-written guard somewhere up the call - three of them, in three different places, none of
        /// which the next implementer will know to copy.
        /// </para>
        /// <para>
        /// <b>Deliberately has no default</b>, so the compiler asks. A default of <c>false</c> would be
        /// safe and would also let the next implementer never think about it; requiring the member means
        /// the question is answered once, explicitly, by the person who knows. (Down-level targets have no
        /// default interface members anyway, so this costs nothing.)
        /// </para>
        /// <para>
        /// Saying <c>true</c> is a claim about <c>WriteImpl</c>: that it renders a complete, correct
        /// command on its own - as the script messages do, falling back to the body-carrying spelling when
        /// no hash was resolved.
        /// </para>
        /// </remarks>
        bool CanWriteWithoutExpansion { get; }
    }
}
