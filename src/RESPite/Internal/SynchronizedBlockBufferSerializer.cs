using RESPite.Messages;

namespace RESPite.Internal;

internal partial class BlockBufferSerializer
{
    internal static BlockBufferSerializer Create() => new SynchronizedBlockBufferSerializer();

    /// <summary>
    /// Used for things like <see cref="RespBatch"/>.
    /// </summary>
    private sealed class SynchronizedBlockBufferSerializer : BlockBufferSerializer
    {
        private protected override BlockBuffer? Buffer { get; set; } // simple per-instance auto-prop

        // use lock-based synchronization
        public override ReadOnlyMemory<byte> Serialize<TRequest>(
            ReadOnlySpan<byte> command,
            in TRequest request,
            IRespFormatter<TRequest> formatter,
            out IDisposable? block)
        {
            bool haveLock = false;
            try // note that "lock" unrolls to something very similar; we're not adding anything unusual here
            {
                // in reality, we *expect* people to not attempt to use batches concurrently, *and*
                // we expect serialization to be very fast, but: out of an abundance of caution,
                // add a timeout - just to avoid surprises (since people can write their own formatters)
                Monitor.TryEnter(this, LockTimeout, ref haveLock);
                if (!haveLock) ThrowTimeout();
                return base.Serialize(command, in request, formatter, out block);
            }
            finally
            {
                if (haveLock) Monitor.Exit(this);
            }

            static void ThrowTimeout() => throw new TimeoutException(
                "It took a long time to get access to the serialization-buffer. This is very odd - please " +
                "ask on GitHub, but *as a guess*, you have a custom RESP formatter that is really slow *and* " +
                "you are using concurrent access to a RESP batch / transaction.");
        }

        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    }
}
