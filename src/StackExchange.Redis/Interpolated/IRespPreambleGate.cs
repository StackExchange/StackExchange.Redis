namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// A condition that a composed preamble establishes, so the pair can skip sending it when it already
    /// holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The write-time half of "always <c>EVALSHA</c>, with <c>SCRIPT LOAD</c> in front of it when needed".
    /// Whether it is needed cannot be known when the frames are rendered, because the connection - and so
    /// the endpoint whose script cache is in question - is not chosen until the write.
    /// </para>
    /// <para>
    /// <b>The belief is soft on purpose, in both directions.</b> Believing wrongly that the condition holds
    /// costs a <c>NOSCRIPT</c> and a retry; believing wrongly that it does not costs a redundant
    /// <c>SCRIPT LOAD</c>, which is idempotent. Neither is damaging, which is what lets this be an
    /// optimisation layered on something already correct rather than a thing the correctness rests on.
    /// </para>
    /// <para>
    /// The connection is passed rather than the endpoint because the right <i>scope</i> differs by
    /// preamble: a loaded script is server-wide, where a connection-local setting is not. Each
    /// implementation picks its own, and gets the reconnect behaviour that goes with it.
    /// </para>
    /// </remarks>
    internal interface IRespPreambleGate
    {
        /// <summary>Whether the preamble still needs to be sent on this connection.</summary>
        bool IsNeeded(PhysicalConnection connection);

        /// <summary>Record that the preamble's effect now holds, having seen it succeed.</summary>
        /// <remarks>
        /// Called when the reply lands, not when the write happens - the same point at which
        /// <c>ResultProcessor.ScriptLoad</c> records the existing path's belief. Recording on send would
        /// claim an effect the server has not yet confirmed.
        /// </remarks>
        void OnEstablished(PhysicalConnection connection);
    }
}
