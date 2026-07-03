using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RESPite.Proxy;

internal sealed partial class WorkerPool : IDisposable
{
#if DEBUG
    private event Action<string>? Log;
#endif
    [Conditional("DEBUG")]
    internal void AddLog(Action<string> value)
    {
#if DEBUG
        Log += value;
#endif
    }

    [Conditional("DEBUG")]
    private void OnLog(ref DefaultInterpolatedStringHandler value)
    {
#if DEBUG
        if (Log is { } log)
        {
            Log?.Invoke(value.ToStringAndClear());
        }
        else
        {
#if NET10_0_OR_GREATER
            value.Clear();
#elif NET8_0_OR_GREATER
            Clear(ref value);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = nameof(Clear))]
            static extern void Clear(ref DefaultInterpolatedStringHandler value);
#endif
        }
#endif
    }

    public WorkerPool()
    {
        var thread = new Thread(static state => ((WorkerPool)state!).Execute());
        thread.Priority = ThreadPriority.AboveNormal;
        thread.IsBackground = true;
        thread.Name = "dedicated worker";
        thread.Start(this);
    }

    private readonly partial struct WorkItem(object target, WorkerStep step, object? arg)
    {
        public object Target => target;
        public WorkerStep Step => step;
        public partial void Execute();
    }

    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly object _syncLock = new();
    private int _flags;

    private const int FlagsSleeping = 1, FlagsDisposed = 2;

    private bool IsSleeping => (Volatile.Read(ref _flags) & FlagsSleeping) != 0;
    private bool IsDisposed => (Volatile.Read(ref _flags) & FlagsDisposed) != 0;

    public bool HasWork => !_queue.IsEmpty;

    public void Enqueue(object target, WorkerStep step, object? arg = null)
    {
        if (IsDisposed) return;
        _queue.Enqueue(new(target, step, arg));
        if (IsSleeping)
        {
            lock (_syncLock)
            {
                if (IsSleeping) // double-checked
                {
                    Monitor.Pulse(_syncLock);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _flags |= FlagsDisposed;
            if (IsSleeping) // double-checked
            {
                Monitor.Pulse(_syncLock);
            }
        }
    }

    [ThreadStatic]
    private static WorkerPool? _current;

    public static WorkerPool? Current => _current;

    public static WorkerPool Demand()
    {
        return _current ?? Throw();
        static WorkerPool Throw() => throw new InvalidOperationException("The current thread is not a pool worker.");
    }

    [Conditional("DEBUG")]
    internal static void DebugAssertWorker()
    {
        Debug.Assert(_current is not null, "dedicated worker expected");
    }

    private void Execute()
    {
        _current = this;
        try
        {
            while (!IsDisposed)
            {
                // Hot Path: Drain everything lock-free
                while (_queue.TryDequeue(out var item))
                {
                    try
                    {
                        OnLog(
                            $"[{Thread.CurrentThread.ManagedThreadId}:{Thread.CurrentThread.Name}] {item.Step} on {item.Target}");
                        item.Execute();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error in {item.Step} ({item.Target}): {ex.Message}");
                    }
                }

                lock (_syncLock)
                {
                    if (_queue.IsEmpty)
                    {
                        _flags |= FlagsSleeping;
                        Monitor.Wait(_syncLock);
                        _flags &= ~FlagsSleeping;
                    }
                }
            }
        }
        finally
        {
            _current = null;
        }
    }
}
