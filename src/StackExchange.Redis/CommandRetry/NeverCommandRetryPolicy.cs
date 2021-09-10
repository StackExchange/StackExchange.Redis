namespace StackExchange.Redis
{
    /// <summary>
    /// Command retry policy to act as a no-op for all commands.
    /// </summary>
    public sealed class NeverCommandRetryPolicy : CommandRetryPolicy
    {
        /// <summary>
        /// Creates a <see cref="NeverCommandRetryPolicy"/> for the given <see cref="ConnectionMultiplexer"/>.
        /// </summary>
        /// <param name="muxer">The <see cref="ConnectionMultiplexer"/> to handle retries for.</param>
        internal NeverCommandRetryPolicy(ConnectionMultiplexer muxer) : base(muxer) { }

        /// <summary>
        /// Gets the current length of the retry queue, always 0.
        /// </summary>
        public override int CurrentQueueLength => 0;

        /// <summary>
        /// Returns whether the current queue is processing (e.g. retrying queued commands).
        /// </summary>
        public override bool CurrentlyProcessing => false;

        /// <summary>
        /// Returns idle, since this queue never does anything
        /// </summary>
        public override string StatusDescription => "Idle";

        /// <summary>
        /// Doesn't queue anything.
        /// </summary>
        protected internal override bool TryQueue(FailedCommand command) => false;

        /// <summary>
        /// Called on heartbeat, evaluating if anything in queue has timed out and need pruning.
        /// </summary>
        public override void OnHeartbeat() { }

        /// <summary>
        /// Called on a multiplexer reconnect, to start sending anything in the queue.
        /// </summary>
        public override void OnReconnect() { }
    }
}
