using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace RESPite.Internal.Buffers;

internal sealed partial class RefCountedSequenceSegment<T> : ReadOnlySequenceSegment<T>, IMemoryOwner<T>
{
#if DEBUGTRACE
    private readonly long _id = Interlocked.Increment(ref _debugTotalLeased);
    private static long _debugTotalLeased, _debugTotalReturned;
    internal static long DebugOutstanding => Volatile.Read(ref _debugTotalLeased) - Volatile.Read(ref _debugTotalReturned);
    internal static long DebugTotalLeased => Volatile.Read(ref _debugTotalLeased);
    partial void DebugDecrOutstanding()
    {
        Interlocked.Increment(ref _debugTotalReturned);
    }
    partial void DebugMessage(string message) => Debug.WriteLine($"[{_id}@{Volatile.Read(ref _refCount)}]: {message}");
#endif
    [Conditional("DEBUGTRACE")]
    partial void DebugMessage([CallerMemberName] string message = "");
    [Conditional("DEBUGTRACE")]
    partial void DebugDecrOutstanding();

    public override string ToString() => $"(ref-count: {RefCount}) {base.ToString()}";
    private int _refCount;
    private readonly IDisposable _handle;
    internal int RefCount => Volatile.Read(ref _refCount);
    private static void ThrowDisposed() => throw new ObjectDisposedException(nameof(RefCountedSequenceSegment<T>));
    private sealed class DisposedMemoryManager : MemoryManager<T>
    {
        public static readonly ReadOnlyMemory<T> Instance;
        private static readonly bool _triggered;
        static DisposedMemoryManager()
        {
            // accessing .Memory touches .Span for .Length, so
            // we need to delay making it throw
            Instance = new DisposedMemoryManager().Memory;
            _triggered = true;
        }

        protected override void Dispose(bool disposing) { }

        // note that we deliberately spoof a non-empty length, to avoid IsEmpty short-circuits,
        // because we *want* people to know that they're doing something wrong;
        // This transient is only used for the initial pre-trigger scenario.
        public override Span<T> GetSpan()
        {
            if (_triggered) ThrowDisposed();
            return new T[8];
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            if (_triggered) ThrowDisposed();
            return default;
        }

        public override void Unpin() { if (_triggered) ThrowDisposed(); }
        protected override bool TryGetArray(out ArraySegment<T> segment)
        {
            if (_triggered) ThrowDisposed();
            segment = default;
            return default;
        }
    }

    public RefCountedSequenceSegment(IDisposable handle, Memory<T> memory, RefCountedSequenceSegment<T>? previous = null)
    {
        _handle = handle;
        _refCount = 1;
        Memory = memory;
        if (previous is not null)
        {
            RunningIndex = previous.RunningIndex + previous.Memory.Length;
            previous.Next = this;
        }
        DebugMessage();
    }

    Memory<T> IMemoryOwner<T>.Memory => MemoryMarshal.AsMemory(Memory);

    void IDisposable.Dispose() => Release();
    public void Release()
    {
        int oldCount;
        do
        {
            oldCount = Volatile.Read(ref _refCount);
            if (oldCount == 0) return; // already released
        }
        while (Interlocked.CompareExchange(ref _refCount, oldCount - 1, oldCount) != oldCount);
        DebugMessage();
        if (oldCount == 1) // then we killed it
        {
            FinalRelease();
        }
    }

    public void AddRef()
    {
        int oldCount;
        do
        {
            oldCount = Volatile.Read(ref _refCount);
            if (oldCount == 0) ThrowDisposed();
        }
        while (Interlocked.CompareExchange(ref _refCount, checked(oldCount + 1), oldCount) != oldCount);
        DebugMessage();
    }

    private void FinalRelease()
    {
        var memory = Memory;
        Memory = DisposedMemoryManager.Instance;
        _handle.Dispose();
        DebugDecrOutstanding();
        DebugMessage();
    }

    internal new RefCountedSequenceSegment<T>? Next
    {
        get => (RefCountedSequenceSegment<T>?)base.Next;
        set => base.Next = value;
    }
}
