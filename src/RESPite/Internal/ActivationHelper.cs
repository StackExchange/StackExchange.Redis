using System.Buffers;
using System.Diagnostics;

namespace RESPite.Internal;

internal static class ActivationHelper
{
    private sealed class WorkItem
#if NETCOREAPP3_0_OR_GREATER
        : IThreadPoolWorkItem
#endif
    {
        private WorkItem()
        {
#if NET5_0_OR_GREATER
            System.Runtime.CompilerServices.Unsafe.SkipInit(out _payload);
#else
            _payload = [];
#endif
        }

        private void Init(byte[] payload, int length, in RespOperation message)
        {
            _payload = payload;
            _length = length;
            _message = message;
        }

        private byte[] _payload;
        private int _length;
        private RespOperation _message;

        private static WorkItem? _spare; // do NOT use ThreadStatic - different producer/consumer, no overlap

        public static void UnsafeQueueUserWorkItem(
            in RespOperation message,
            ReadOnlySpan<byte> payload,
            ref byte[]? lease)
        {
            if (lease is null)
            {
                // we need to create our own copy of the data
                lease = ArrayPool<byte>.Shared.Rent(payload.Length);
                payload.CopyTo(lease);
            }

            var obj = Interlocked.Exchange(ref _spare, null) ?? new();
            obj.Init(lease, payload.Length, message);
            lease = null; // count as claimed

            DebugCounters.OnCopyOut(payload.Length);
#if NETCOREAPP3_0_OR_GREATER
            ThreadPool.UnsafeQueueUserWorkItem(obj, false);
#else
            ThreadPool.UnsafeQueueUserWorkItem(WaitCallback, obj);
#endif
        }
#if !NETCOREAPP3_0_OR_GREATER
        private static readonly WaitCallback WaitCallback = state => ((WorkItem)state!).Execute();
#endif

        public void Execute()
        {
            var message = _message;
            var payload = _payload;
            var length = _length;
            _message = default;
            _payload = [];
            _length = 0;
            Interlocked.Exchange(ref _spare, this);
            var msg = message;
            msg.Message.TrySetResult(msg.Token, new ReadOnlySpan<byte>(payload, 0, length));
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    public static void ProcessResponse(in RespOperation pending, ReadOnlySpan<byte> payload, ref byte[]? lease)
    {
        var msg = pending.Message;
        if (msg.AllowInlineParsing)
        {
            msg.TrySetResult(pending.Token, payload);
        }
        else
        {
            WorkItem.UnsafeQueueUserWorkItem(pending, payload, ref lease);
        }
    }

    private static readonly Action<object?> CancellationCallback = static state
        => ((RespMessageBase)state!).TrySetCanceledTrustToken();

    public static CancellationTokenRegistration RegisterForCancellation(
        RespMessageBase message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return cancellationToken.Register(CancellationCallback, message);
    }

    [Conditional("DEBUG")]
    public static void DebugBreak()
    {
#if DEBUG
        if (Debugger.IsAttached) Debugger.Break();
#endif
    }

    [Conditional("DEBUG")]
    public static void DebugBreakIf(bool condition)
    {
#if DEBUG
        if (condition && Debugger.IsAttached) Debugger.Break();
#endif
    }
}
