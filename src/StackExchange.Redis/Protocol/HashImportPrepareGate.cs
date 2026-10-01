namespace StackExchange.Redis
{
    /// <summary>
    /// The write-time half of "always <c>HIMPORT SET</c>, with <c>HIMPORT PREPARE</c> in front of it when
    /// this connection has not seen the field-set".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Connection-local, where <see cref="ScriptLoadGate"/> is server-wide</b> - which is the reason
    /// <see cref="IRespPreambleGate"/> is handed the connection rather than the endpoint. A loaded script
    /// is a property of the server; a prepared field-set is a property of the session, so a reconnect
    /// gives a fresh, empty set and re-prepares automatically.
    /// </para>
    /// <para>
    /// <b>The claim happens in <see cref="IsNeeded"/>, not in <see cref="OnEstablished"/>, and that is
    /// deliberate.</b> The interface's usual arrangement records the belief when the reply lands, so
    /// nothing is claimed that the server has not confirmed. This one cannot afford it: it runs inside the
    /// write lock, which is the only place where "has this connection prepared it?" and "write it" are one
    /// decision - and a burst of imports issued before the first PREPARE's reply landed would otherwise
    /// each inject their own. Measured on the frame-surface probe, and the same reasoning as the shipped
    /// <c>HashImportSetMessage.GetMessages</c>, which claims in exactly the same place.
    /// </para>
    /// <para>
    /// A claim that then fails to write dies with the connection, which starts empty - so the failure mode
    /// of claiming early is bounded, where the failure mode of claiming late is an unbounded burst of
    /// redundant preambles.
    /// </para>
    /// </remarks>
    /// <param name="fieldSet">The field-set whose <c>PREPARE</c> this gates.</param>
    /// <param name="database">
    /// The database the import runs against, recorded so a later <c>DISCARD</c> targets the right one. The
    /// connection cannot answer it - a bridge is per server, not per database - so it comes from the
    /// context that composed the pair.
    /// </param>
    internal sealed class HashImportPrepareGate(HashImport fieldSet, int database) : IRespPreambleGate
    {
        /// <summary>Whether this connection still needs the <c>PREPARE</c>; claims it if so.</summary>
        /// <param name="connection">The connection the pair is about to be written on.</param>
        public bool IsNeeded(PhysicalConnection connection)
        {
            if (!connection.TryAddPreparedFieldSet(fieldSet.Id)) return false; // already prepared here

            // recorded for disposal's benefit, at node granularity, in the same breath as the claim - as
            // the shipped path does. Bookkeeping only: it is inside the write lock, so it issues no I/O.
            var server = connection.BridgeCouldBeNull?.ServerEndPoint;
            if (server is not null) fieldSet.RegisterServer(server, database);
            return true;
        }

        /// <summary>Nothing to do: <see cref="IsNeeded"/> already claimed it.</summary>
        /// <param name="connection">The connection the preamble was written on.</param>
        public void OnEstablished(PhysicalConnection connection)
        {
        }
    }
}
