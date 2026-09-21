namespace StackExchange.Redis
{
    /// <summary>
    /// What a <see cref="IRespPreambleGate"/> needs to know about the connection a pair is being written
    /// on, in terms both cores can answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because the gate used to take a <c>PhysicalConnection</c>.</b> That is an old-core
    /// type, so the whole preamble mechanism was unreachable from the new core - recorded in design notes
    /// section 7s as an optimisation gap, and later found to be a correctness one too, since injecting a
    /// <c>SELECT</c> before a command for another database is the same capability.
    /// </para>
    /// <para>
    /// <b>Two questions, at two different scopes, and the distinction is the whole design.</b> A loaded
    /// script is <i>server-wide</i>: load it on one connection and every connection to that server can use
    /// it, which is why the belief hangs off the endpoint. A prepared field-set is <i>connection-local</i>:
    /// another connection to the same server has not prepared it. Conflating them would either re-send
    /// preambles that were not needed or skip ones that were.
    /// </para>
    /// </remarks>
    internal interface IRespPreambleTarget
    {
        /// <summary>The server this connection reaches, for beliefs that are server-wide.</summary>
        /// <remarks>
        /// Null when it cannot be determined, which a gate must read as "no idea" rather than "no": a
        /// redundant <c>SCRIPT LOAD</c> is harmless, a skipped one is a <c>NOSCRIPT</c>.
        /// </remarks>
        ServerEndPoint? Server { get; }

        /// <summary>Claim a connection-local fact; false if this connection already holds it.</summary>
        /// <param name="id">Identifies the fact - a field-set id, today.</param>
        bool TryClaim(long id);
    }
}
