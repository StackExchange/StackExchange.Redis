namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Answers "what can the server that would receive this actually do?" - the one
    /// question a <see cref="RespContext"/> cannot answer for itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A context knows its command map, its database and its server type. It does <b>not</b> know a
    /// version, and cannot: a version belongs to an endpoint, and the endpoint is chosen after the frame
    /// is rendered. That is fine for almost everything - the bytes do not depend on the server - but it
    /// bites whenever the library wants to send a <i>newer spelling of the same request</i>:
    /// <c>SET ... GET</c> for <c>GETSET</c>, <c>LMOVE</c> for <c>RPOPLPUSH</c>, <c>BITFIELD_RO</c> for an
    /// all-read <c>BITFIELD</c>. Each is strictly better where it exists and an unknown-command error
    /// where it does not.
    /// </para>
    /// <para>
    /// <b>A service rather than a method on <see cref="RespExecutorBase"/></b>, deliberately. Executors are
    /// daisy-chained - cache in front of retry in front of dispatch - so a new interface member would have
    /// to be threaded through every decorator, including any written outside this library. A service is
    /// looked up by type from the context, so a decorator that knows nothing about it stays correct, and
    /// the shape can evolve without breaking anyone. It is internal for the same reason: this is
    /// <see cref="RedisCommand"/>-shaped, and none of that is public.
    /// </para>
    /// <para>
    /// <b>Absence is a supported answer.</b> A context with no such service - a bare
    /// <c>new RespContext()</c>, a test fake, an executor someone wired up by hand - reports nothing
    /// known, and the caller falls back to the spelling that works everywhere. That is what makes this
    /// safe to add one command at a time.
    /// </para>
    /// </remarks>
    internal interface IRespServerFeatures
    {
        /// <summary>
        /// The features of the server this command would reach.
        /// </summary>
        /// <param name="command">The command whose routing decides which server answers.</param>
        /// <param name="key">The key being addressed, or <c>default</c> for a command that routes to no particular key.</param>
        /// <param name="flags">The command's flags; replica preferences change which server is picked.</param>
        /// <param name="features">
        /// The best answer available: the selected server's version, or the configured default version when
        /// no server could be selected. Populated either way.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when a live server supplied the version, <see langword="false"/> when
        /// <paramref name="features"/> is the configured fallback rather than something observed.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Both halves are useful, which is why this is not simply <c>RedisFeatures GetFeatures(...)</c>.
        /// Choosing between two spellings of a command wants the <see langword="bool"/> - guessing wrong
        /// costs an error, so "not sure" should mean "use the old one". Anything merely sizing a request
        /// can take <paramref name="features"/> and ignore the return, which is what the
        /// <c>MessageWriter</c> path does today.
        /// </para>
        /// <para>
        /// <b>The key arrives fully composed.</b> It is used to pick an endpoint, so in a cluster it decides
        /// which node's version is reported - which means it has to be the bytes that will actually go on
        /// the wire. On this surface the key prefix is context state applied at write time, not something
        /// already baked into the <see cref="RedisKey"/> by a <c>KeyPrefixed*</c> decorator, so
        /// <see cref="RespContext.TryGetFeatures"/> composes the two before calling here; an implementation
        /// must not prefix again. A <c>default</c> key means the command routes to no particular key and is
        /// passed through unprefixed rather than turned into a key that is only the prefix.
        /// </para>
        /// <para>
        /// Even so the answer is a sample rather than a promise: in a mixed-version cluster mid-upgrade the
        /// node can change between this call and the write. The existing <c>GetFeatures</c> has the
        /// identical race, and the fallback for a wrong guess is an unknown-command error on one call.
        /// </para>
        /// </remarks>
        bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures features);
    }
}
