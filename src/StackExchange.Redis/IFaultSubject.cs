namespace StackExchange.Redis
{
    /// <summary>
    /// What a fault report needs to know about the command it is reporting on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Introduced so that <see cref="ExceptionFactory"/> stops requiring a <c>Message</c>.</b> Everything
    /// a timeout or connection fault says about the command - what it was, which key it named, how far it
    /// got, which connection type it wanted - is answerable by any command representation; the dependency on
    /// one particular class was incidental, and it meant the new core could not produce the diagnostics that
    /// callers have been reading for years. Those diagnostics are the whole value of the exception: "Timeout
    /// performing GET" without <c>qs</c>, <c>in</c>, <c>last-in</c> or the thread-pool counters is a
    /// statement that something was slow, not a diagnosis.
    /// </para>
    /// <para>
    /// Deliberately the small subset that is common ground. The bridge-specific extras - the head-of-queue
    /// messages, the physical read/write state - stay behind a cast to <c>Message</c>, because they describe
    /// a <c>PhysicalBridge</c> rather than a command, and there is nothing to lose by omitting them from a
    /// core that has no bridge.
    /// </para>
    /// </remarks>
    internal interface IFaultSubject
    {
        /// <summary>The command, and the key it named when there is one.</summary>
        string CommandAndKey { get; }

        /// <summary>The command.</summary>
        RedisCommand Command { get; }

        /// <summary>The command alone, for when detail is not to be included in exceptions.</summary>
        string CommandString { get; }

        /// <summary>The flags the caller issued this command with.</summary>
        CommandFlags Flags { get; }

        /// <summary>How far this command got.</summary>
        CommandStatus Status { get; }

        /// <summary>Whether this command was waiting for a connection rather than for a reply.</summary>
        bool IsBacklogged { get; }

        /// <summary>Whether the caller awaited this command rather than blocking on it.</summary>
        bool IsAsync { get; }

        /// <summary>Whether this command belongs on the subscription connection.</summary>
        bool IsForSubscriptionBridge { get; }

        /// <summary>The slot this command's key hashes to, or <see cref="ServerSelectionStrategy.NoSlot"/>.</summary>
        /// <param name="serverSelectionStrategy">Supplies the hashing, which is a property of the deployment.</param>
        int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy);
    }
}
