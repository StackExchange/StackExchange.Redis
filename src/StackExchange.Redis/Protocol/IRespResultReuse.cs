namespace StackExchange.Redis.Protocol
{
    /// <summary>
    /// Additionally implemented by an <see cref="IRespHandler{TResult}"/> whose parsed results may be
    /// handed to more than one caller, so a client-side cache hit can skip deserialization and serve the
    /// result it already has.
    /// </summary>
    /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
    /// <remarks>
    /// <para>
    /// The cache stores the raw reply; without this, every hit re-parses it. The core shares automatically
    /// only what it can prove safe - strings, and value types without inner references - because sharing a
    /// mutable object between callers is corruption, not caching. A handler that <i>knows</i> its results
    /// are safe - immutable types, records with no settable state, a deserializer applying rules such as
    /// the HybridCache immutability conventions - says so here, and its results are memoized too.
    /// </para>
    /// <para>
    /// Asked per value rather than per type, so the answer can depend on what was actually produced: a
    /// polymorphic declared type whose runtime type decides, or content the deserializer only trusts in
    /// some shapes. An implementation with a purely type-level rule simply returns a constant.
    /// </para>
    /// <para>
    /// Reuse is keyed by handler <b>instance</b>: a memoized result is only served back through the same
    /// handler object that produced it, so implementations should be singletons (as the inbuilt handlers
    /// are) for the memoization to pay.
    /// </para>
    /// </remarks>
    public interface IRespResultReuse<TResult>
    {
        /// <summary>Whether this value may be handed to more than one caller.</summary>
        /// <param name="value">The value just parsed.</param>
        bool MayReuse(in TResult value);
    }
}
