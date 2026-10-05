namespace StackExchange.Redis
{
    /// <summary>
    /// What a <see cref="IRespPreambleGate"/> needs to know about the connection a pair is being written
    /// on, in terms any connection can answer.
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

        /// <summary>
        /// Move this connection onto <paramref name="database"/>, reporting whether a <c>SELECT</c> has to
        /// be written to make that true.
        /// </summary>
        /// <param name="database">The database the command about to be written belongs to.</param>
        /// <returns>
        /// <see langword="true"/> if the caller must write a <c>SELECT</c> immediately before its command;
        /// <see langword="false"/> if the connection is already there.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>Asks and claims in one call, because they cannot be separated.</b> Reading the current
        /// database, deciding, and then writing is three steps another sender can interleave: it sees the
        /// database this caller is about to set, concludes it needs no <c>SELECT</c>, and wins the write
        /// lock - so its command runs against a database that has not been selected yet. The only safe
        /// shape is to claim while holding the lock that orders the writes, which is what the conditional
        /// pair-send does.
        /// </para>
        /// <para>
        /// A target that manages its own database - as the v3 bridge did - answers
        /// <see langword="false"/>: nothing for this mechanism to inject.
        /// </para>
        /// </remarks>
        bool TrySelectDatabase(int database);
    }
}
