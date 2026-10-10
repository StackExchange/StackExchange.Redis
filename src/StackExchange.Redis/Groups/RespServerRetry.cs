namespace StackExchange.Redis
{
    /// <summary>
    /// The retry categories the server commands are issued under.
    /// </summary>
    /// <remarks>
    /// <b>Several server commands are one <see cref="RedisCommand"/> covering wildly different verbs</b> -
    /// <c>CLIENT</c>, <c>CLUSTER</c>, <c>CONFIG</c>, <c>MEMORY</c>, <c>LATENCY</c> - so the whole-command
    /// default has to assume the most side-effecting sub-command. Where the sub-command is known, it can
    /// be accurate instead, which is what these two are for. Everything they describe is node-scoped: the
    /// answer, or the effect, belongs to the server that was asked.
    /// </remarks>
    internal static class RespServerRetry
    {
        /// <summary>A read whose answer belongs to the node that was asked.</summary>
        internal const CommandFlags NodeLocalRead = CommandFlags.CommandRetryReadOnly | CommandFlagsInternal.CommandServerSpecific;

        /// <summary>An administrative action on the node that was asked.</summary>
        internal const CommandFlags NodeLocalAdmin = CommandFlags.CommandRetryServerAdmin | CommandFlagsInternal.CommandServerSpecific;
    }
}
