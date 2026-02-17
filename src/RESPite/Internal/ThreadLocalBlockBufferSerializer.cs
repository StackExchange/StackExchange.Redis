namespace RESPite.Internal;

internal partial class BlockBufferSerializer
{
    internal static BlockBufferSerializer Shared => ThreadLocalBlockBufferSerializer.Instance;
    private sealed class ThreadLocalBlockBufferSerializer : BlockBufferSerializer
    {
        private ThreadLocalBlockBufferSerializer() { }
        public static readonly ThreadLocalBlockBufferSerializer Instance = new();

        [ThreadStatic]
        // side-step concurrency using per-thread semantics
        private static BlockBuffer? _perTreadBuffer;

        private protected override BlockBuffer? Buffer
        {
            get => _perTreadBuffer;
            set => _perTreadBuffer = value;
        }
    }
}
