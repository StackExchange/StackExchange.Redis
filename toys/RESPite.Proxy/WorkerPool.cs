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
    internal void AddDebugLog(Action<string> value)
    {
#if DEBUG
        Log += value;
#endif
    }

    [Conditional("DEBUG")]
    private void OnLog(string value)
    {
#if DEBUG
        Log?.Invoke(value);
#endif
    }

#if NET8_0_OR_GREATER
    [Conditional("DEBUG")]
    private void OnLog(ref DefaultInterpolatedStringHandler value)
    {
#if DEBUG
        if (Log is { } log)
        {
            log(value.ToStringAndClear());
        }
        else
        {
#if NET10_0_OR_GREATER
            value.Clear();
#else
            Clear(ref value);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Clear")]
            static extern void Clear(ref DefaultInterpolatedStringHandler value);
#endif
        }
#endif
    }
#endif

    public WorkerPool(int workers = 0)
    {
        if (workers < 1) workers = Environment.ProcessorCount;

        // any worker can run any item; per-connection serialization is guaranteed by the SAEA
        // completion chain and the ActiveWriter flag, not by there being a single thread.
        for (int i = 0; i < workers; i++)
        {
            var thread = new Thread(static state => ((WorkerPool)state!).Execute())
            {
                Priority = ThreadPriority.AboveNormal,
                IsBackground = true,
                Name = workers == 1 ? "dedicated worker" : $"dedicated worker {i}",
            };
            thread.Start(this);
        }
    }

    private readonly partial struct WorkItem(object target, WorkerStep step, object? arg)
    {
        public object Target => target;
        public WorkerStep Step => step;
        public partial void Execute();
    }

    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly object _syncLock = new();

    // number of workers currently blocked in Monitor.Wait; only mutated under _syncLock. Reading it
    // (unlocked) on the enqueue hot-path is just a "should I bother taking the lock to pulse?" hint.
    private int _waiting;
    private volatile bool _disposed;

    private bool IsDisposed => _disposed;

    public bool HasWork => !_queue.IsEmpty;

    [ThreadStatic]
    private static int _inlineDepth;

    public void Enqueue(object target, WorkerStep step, object? arg = null, bool inline = false)
    {
        if (_disposed) return;

        WorkItem op = new(target, step, arg);
        const int MAX_INLINE_DEPTH = 2; // prevent IO stack-dive
        if (inline && _inlineDepth < MAX_INLINE_DEPTH)
        {
            _inlineDepth++;
            try
            {
                op.Execute();
            }
            catch { }
            _inlineDepth--;
            return;
        }
        _queue.Enqueue(op);

        // wake one worker per item; the re-check under the lock is authoritative and closes the
        // "consumer decided to wait but hadn't incremented _waiting yet" race (it re-checks the
        // queue under the same lock before waiting, so it can't miss this item).
        if (Volatile.Read(ref _waiting) > 0)
        {
            lock (_syncLock)
            {
                if (_waiting > 0) Monitor.Pulse(_syncLock);
            }
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _disposed = true;
            Monitor.PulseAll(_syncLock); // wake every worker so they can observe disposal and exit
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
        using var control = ExecutionContext.SuppressFlow();
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
                    // re-check inside the lock: an item enqueued between the drain and here is visible,
                    // so we won't wait on a non-empty queue (and thus can't miss its Pulse).
                    if (_queue.IsEmpty && !_disposed)
                    {
                        _waiting++;
                        Monitor.Wait(_syncLock);
                        _waiting--;
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
