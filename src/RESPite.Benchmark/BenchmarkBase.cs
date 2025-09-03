using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

// influenced by redis-benchmark, see .md file
namespace RESPite.Benchmark;

public abstract class BenchmarkBase : IDisposable
{
    protected const string
        _getSetKey = "key:__rand_int__",
        _counterKey = "counter:__rand_int__",
        _listKey = "mylist",
        _setKey = "myset",
        _hashKey = "myhash",
        _sortedSetKey = "myzset",
        _streamKey = "mystream";

    public PipelineStrategy PipelineMode { get; } =
        PipelineStrategy.Batch; // the default, for parity with how redis-benchmark works

    public enum PipelineStrategy
    {
        /// <summary>
        /// Build a batch of operations, send them all at once.
        /// </summary>
        Batch,

        /// <summary>
        /// Use a queue to pipeline operations - when we hit the pipeline depth, we pop one, push one, await the popped
        /// </summary>
        Queue,
    }

    private readonly HashSet<string> _tests = new(StringComparer.OrdinalIgnoreCase);
    protected bool RunTest(string name) => _tests.Count == 0 || _tests.Contains(name);
    public virtual void Dispose() { }
    public int Port { get; } = 6379;
    public int PipelineDepth { get; } = 1;
    public bool Multiplexed { get; }
    public bool SupportCancel { get; }
    public bool Loop { get; }
    public bool Quiet { get; }
    public int ClientCount { get; } = 50;
    public int OperationsPerClient { get; }

    public int TotalOperations => OperationsPerClient * ClientCount;

    protected readonly byte[] _payload;

    public BenchmarkBase(string[] args)
    {
        int operations = 100_000;

        string tests = "";
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-p" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                    Port = tmp;
                    break;
                case "-c" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                    ClientCount = tmp;
                    break;
                case "-n" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                    operations = tmp;
                    break;
                case "-P" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                    PipelineDepth = tmp;
                    break;
                case "+m":
                    Multiplexed = true;
                    break;
                case "-m":
                    Multiplexed = false;
                    break;
                case "+x":
                    SupportCancel = true;
                    break;
                case "-c":
                    SupportCancel = false;
                    break;
                case "-l":
                    Loop = true;
                    break;
                case "-q":
                    Quiet = true;
                    break;
                case "-t" when i != args.Length - 1:
                    tests = args[++i];
                    break;
                case "--batch":
                    PipelineMode = PipelineStrategy.Batch;
                    break;
                case "--queue":
                    PipelineMode = PipelineStrategy.Queue;
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(tests))
        {
            foreach (var test in tests.Split(','))
            {
                var t = test.Trim();
                if (!string.IsNullOrWhiteSpace(t)) _tests.Add(t);
            }
        }

        OperationsPerClient = operations / ClientCount;

        _payload = "abc"u8.ToArray();
    }

    public abstract Task RunAll();
}

public abstract class BenchmarkBase<TClient>(string[] args) : BenchmarkBase(args)
{
    protected virtual Task OnCleanupAsync(TClient client) => Task.CompletedTask;

    protected virtual Task InitAsync(TClient client) => Task.CompletedTask;

    public async Task CleanupAsync()
    {
        try
        {
            var client = GetClient(0);
            await Delete(client, _getSetKey).ConfigureAwait(false);
            await Delete(client, _counterKey).ConfigureAwait(false);
            await Delete(client, _listKey).ConfigureAwait(false);
            await Delete(client, _setKey).ConfigureAwait(false);
            await Delete(client, _hashKey).ConfigureAwait(false);
            await Delete(client, _sortedSetKey).ConfigureAwait(false);
            await Delete(client, _streamKey).ConfigureAwait(false);
            await OnCleanupAsync(client).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cleanup: {ex.Message}");
        }
    }

    protected virtual ValueTask Flush(TClient client) => default;
    protected virtual void PrepareBatch(TClient client, int count) { }

    private async Task<DBNull> PipelineUntyped(
        TClient client,
        Func<TClient, ValueTask> operation)
    {
        var opsPerClient = OperationsPerClient;
        int i = 0;
        try
        {
            if (PipelineDepth <= 1)
            {
                for (; i < opsPerClient; i++)
                {
                    await operation(client).ConfigureAwait(false);
                }
            }
            else if (PipelineMode == PipelineStrategy.Queue)
            {
                var queue = new Queue<ValueTask>(opsPerClient);
                for (; i < opsPerClient; i++)
                {
                    if (queue.Count == opsPerClient)
                    {
                        await queue.Dequeue().ConfigureAwait(false);
                    }

                    queue.Enqueue(operation(client));
                }

                while (queue.Count > 0)
                {
                    await queue.Dequeue().ConfigureAwait(false);
                }
            }
            else if (PipelineMode == PipelineStrategy.Batch)
            {
                int count = 0;
                var oversized = ArrayPool<ValueTask>.Shared.Rent(PipelineDepth);
                PrepareBatch(client, Math.Min(opsPerClient, PipelineDepth));
                for (; i < opsPerClient; i++)
                {
                    oversized[count++] = operation(client);
                    if (count == PipelineDepth)
                    {
                        await Flush(client).ConfigureAwait(false);
                        PrepareBatch(client, Math.Min(opsPerClient - i, PipelineDepth));
                        for (int j = 0; j < count; j++)
                        {
                            await oversized[j].ConfigureAwait(false);
                        }

                        count = 0;
                    }
                }

                await Flush(client).ConfigureAwait(false);
                for (int j = 0; j < count; j++)
                {
                    await oversized[j].ConfigureAwait(false);
                }

                ArrayPool<ValueTask>.Shared.Return(oversized);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected pipeline mode: {PipelineMode}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{operation.Method.Name} failed after {i} operations");
            Program.WriteException(ex);
        }

        return DBNull.Value;
    }

    private async Task<T> PipelineTyped<T>(TClient client, Func<TClient, ValueTask<T>> operation)
    {
        var opsPerClient = OperationsPerClient;
        int i = 0;
        T result = default!;
        try
        {
            if (PipelineDepth == 1)
            {
                for (; i < opsPerClient; i++)
                {
                    result = await operation(client).ConfigureAwait(false);
                }
            }
            else if (PipelineMode == PipelineStrategy.Queue)
            {
                var queue = new Queue<ValueTask<T>>(opsPerClient);
                for (; i < opsPerClient; i++)
                {
                    if (queue.Count == opsPerClient)
                    {
                        _ = await queue.Dequeue().ConfigureAwait(false);
                    }

                    queue.Enqueue(operation(client));
                }

                while (queue.Count > 0)
                {
                    result = await queue.Dequeue().ConfigureAwait(false);
                }
            }
            else if (PipelineMode == PipelineStrategy.Batch)
            {
                int count = 0;
                var oversized = ArrayPool<ValueTask<T>>.Shared.Rent(PipelineDepth);
                PrepareBatch(client, Math.Min(opsPerClient, PipelineDepth));
                for (; i < opsPerClient; i++)
                {
                    oversized[count++] = operation(client);
                    if (count == PipelineDepth)
                    {
                        await Flush(client).ConfigureAwait(false);
                        PrepareBatch(client, Math.Min(opsPerClient - (i + 1), PipelineDepth));
                        for (int j = 0; j < count; j++)
                        {
                            result = await oversized[j].ConfigureAwait(false);
                        }

                        count = 0;
                    }
                }

                await Flush(client).ConfigureAwait(false);
                for (int j = 0; j < count; j++)
                {
                    result = await oversized[j].ConfigureAwait(false);
                }

                ArrayPool<ValueTask<T>>.Shared.Return(oversized);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected pipeline mode: {PipelineMode}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{operation.Method.Name} failed after {i} operations");
            Program.WriteException(ex);
        }

        return result;
    }

    public async Task InitAsync()
    {
        for (int i = 0; i < ClientCount; i++)
        {
            await InitAsync(GetClient(i)).ConfigureAwait(false);
        }
    }

    protected abstract TClient GetClient(int index);
    protected virtual TClient WithCancellation(TClient client, CancellationToken cancellationToken) => client;
    protected abstract Task Delete(TClient client, string key);

    protected abstract TClient CreateBatch(TClient client);

    protected Task RunAsync<T>(
        string? key,
        Func<TClient, ValueTask<T>> action,
        Func<TClient, ValueTask>? init = null,
        string format = "")
        => RunAsyncCore<T>(
            key,
            action,
            client => action(client).AsUntypedValueTask(),
            client => PipelineTyped<T>(client, action),
            init,
            format);

    protected Task RunAsync(
        string? key,
        Func<TClient, ValueTask> action,
        Func<TClient, ValueTask>? init = null,
        string format = "")
        => RunAsyncCore<DBNull>(key, action, action, client => PipelineUntyped(client, action), init, format);

    protected async Task RunAsyncCore<T>(
        string? key,
        Delegate underlyingAction,
        Func<TClient, ValueTask> test,
        Func<TClient, Task<T>> pipeline,
        Func<TClient, ValueTask>? init = null,
        string format = "")
    {
        string name = underlyingAction.Method.Name;

        if (underlyingAction.Method.GetCustomAttribute(typeof(DisplayNameAttribute)) is DisplayNameAttribute
            {
                DisplayName: { Length: > 0 }
            } dna)
        {
            name = dna.DisplayName;
        }

        // skip test if not needed
        if (!RunTest(name)) return;

        // include additional test metadata
        string description = "";
        if (underlyingAction.Method.GetCustomAttribute(typeof(DescriptionAttribute)) is DescriptionAttribute
            {
                Description: { Length: > 0 }
            } da)
        {
            description = $" ({da.Description})";
        }

        if (Quiet)
        {
            Console.Write($"{name}:");
        }
        else
        {
            Console.Write(
                $"====== {name}{description} ====== (clients: {ClientCount:#,##0}, ops: {TotalOperations:#,##0}");
            if (Multiplexed)
            {
                Console.Write(", mux");
            }

            if (SupportCancel)
            {
                Console.Write(", cancel");
            }

            if (PipelineDepth > 1)
            {
                Console.Write($", {PipelineMode}: {PipelineDepth:#,##0}");
            }
            else
            {
                Console.Write(", sequential");
            }

            Console.WriteLine(")");
        }

        bool didNotRun = false;
        try
        {
            if (key is not null)
            {
                await Delete(GetClient(0), key).ConfigureAwait(false);
            }

            try
            {
                await test(GetClient(0)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\t{ex.Message}");
                didNotRun = true;
                return;
            }

            if (init is not null)
            {
                await init(GetClient(0)).ConfigureAwait(false);
            }

            var pending = new Task<T>[ClientCount];
            int index = 0;
#if DEBUG
            Internal.DebugCounters.Flush();
#endif
            // optionally support cancellation, applied per-test
            CancellationToken cancellationToken = CancellationToken.None;
            using var cts = SupportCancel ? new CancellationTokenSource(TimeSpan.FromSeconds(20)) : null;
            if (SupportCancel) cancellationToken = cts!.Token;

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < ClientCount; i++)
            {
                var client = GetClient(i);
                if (PipelineMode == PipelineStrategy.Batch && PipelineDepth > 1)
                {
                    client = CreateBatch(client);
                }

                pending[index++] = Task.Run(() => pipeline(WithCancellation(client, cancellationToken)));
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
            watch.Stop();

            var seconds = watch.Elapsed.TotalSeconds;
            var rate = TotalOperations / seconds;
            if (Quiet)
            {
                Console.WriteLine($"\t{rate:###,###,##0} requests per second");
                return;
            }
            else
            {
                Console.WriteLine(
                    $"{TotalOperations:###,###,##0} requests completed in {seconds:0.00} seconds, {rate:###,###,##0} ops/sec");
            }

            if (!Quiet & typeof(T) != typeof(DBNull))
            {
                if (string.IsNullOrWhiteSpace(format))
                {
                    format = "Typical result: {0}";
                }

                T result = await pending[pending.Length - 1];
                Console.WriteLine(format, result);
            }
        }
        catch (Exception ex)
        {
            if (Quiet) Console.WriteLine();
            Program.WriteException(ex, name);
        }
        finally
        {
            _ = didNotRun;
#if DEBUG
            var counters = Internal.DebugCounters.Flush(); // flush even if not showing
            if (!Quiet & !didNotRun)
            {
                if (counters.WriteBytes != 0)
                {
                    Console.Write($"Write: {FormatBytes(counters.WriteBytes)}");
                    if (counters.SyncWriteCount != 0) Console.Write($"; {counters.SyncWriteCount:#,##0} sync");
                    if (counters.AsyncWriteInlineCount != 0)
                        Console.Write($"; {counters.AsyncWriteInlineCount:#,##0} async-inline");
                    if (counters.AsyncWriteCount != 0) Console.Write($"; {counters.AsyncWriteCount:#,##0} full-async");
                    Console.WriteLine();
                }

                if (counters.ReadBytes != 0)
                {
                    Console.Write($"Read: {FormatBytes(counters.ReadBytes)}");
                    if (counters.ReadCount != 0) Console.Write($"; {counters.ReadCount:#,##0} sync");
                    if (counters.AsyncReadInlineCount != 0)
                        Console.Write($"; {counters.AsyncReadInlineCount:#,##0} async-inline");
                    if (counters.AsyncReadCount != 0) Console.Write($"; {counters.AsyncReadCount:#,##0} full-async");
                    Console.WriteLine();
                }

                if (counters.DiscardFullCount + counters.DiscardPartialCount != 0)
                {
                    Console.Write($"Discard average: {FormatBytes(counters.DiscardAverage)}");
                    if (counters.DiscardFullCount != 0) Console.Write($"; {counters.DiscardFullCount} full");
                    if (counters.DiscardPartialCount != 0) Console.Write($"; {counters.DiscardPartialCount} partial");
                    Console.WriteLine();
                }

                if (counters.CopyOutCount != 0)
                {
                    Console.WriteLine(
                        $"Copy out: {FormatBytes(counters.CopyOutBytes)}; {counters.CopyOutCount:#,##0} times");
                }

                if (counters.PipelineFullAsyncCount != 0
                    | counters.PipelineSendAsyncCount != 0
                    | counters.PipelineFullSyncCount != 0)
                {
                    Console.Write("Pipelining");
                    if (counters.PipelineFullSyncCount != 0)
                        Console.Write($"; full sync: {counters.PipelineFullSyncCount:#,##0}");
                    if (counters.PipelineSendAsyncCount != 0)
                        Console.Write($"; send async: {counters.PipelineSendAsyncCount:#,##0}");
                    if (counters.PipelineFullAsyncCount != 0)
                        Console.Write($"; full async: {counters.PipelineFullAsyncCount:#,##0}");
                    Console.WriteLine();
                }

                if (counters.BatchWriteCount != 0)
                {
                    Console.Write($"Batching; {counters.BatchWriteCount:#,##0} batches");
                    if (counters.BatchWriteFullPageCount != 0)
                        Console.Write($"; {counters.BatchWriteFullPageCount:#,###,##0} full pages");
                    if (counters.BatchWritePartialPageCount != 0)
                        Console.Write($"; {counters.BatchWritePartialPageCount:#,###,##0} partial pages");
                    if (counters.BatchWriteMessageCount != 0)
                        Console.Write($"; {counters.BatchWriteMessageCount:#,###,##0} messages");
                    Console.WriteLine();
                }

                if (counters.BatchGrowCount != 0)
                {
                    Console.WriteLine(
                        $"Batch growth; {counters.BatchGrowCount:#,##0} events, {counters.BatchGrowCopyCount:#,###,##0} elements copied");
                }

                if (counters.BatchBufferLeaseCount != 0 | counters.BatchMultiRootMessageCount != 0)
                {
                    Console.Write($"Multi-message batching: {counters.BatchMultiRootMessageCount:#,###,##0} batches, {counters.BatchMultiChildMessageCount:#,###,##0} sub-messages");
                    if (counters.BatchBufferLeaseCount != 0)
                        Console.Write($"; {counters.BatchBufferLeaseCount:#,###,##0} blocks leased, {counters.BatchBufferReturnCount:#,###,##0} blocks returned, {counters.BatchBufferElementsOutstanding:#,###,##0} elements outstanding");
                    Console.WriteLine();
                }

                if (counters.BufferCreatedCount != 0 ||
                    counters.BufferRecycledCount != 0 | counters.BufferMessageCount != 0)
                {
                    Console.Write("Buffers");
                    if (counters.BufferCreatedCount != 0)
                    {
                        Console.Write(
                            $"; created: {counters.BufferCreatedCount:#,###,##0}, {FormatBytes(counters.BufferTotalBytes)}");
                        // always write recycled count - it being zero is important
                        Console.Write(
                            $"; recycled: {counters.BufferRecycledCount:#,###,##0}, {FormatBytes(counters.BufferRecycledBytes)}");
                    }

                    if (counters.BufferMessageCount != 0)
                    {
                        Console.Write(
                            $"; {counters.BufferMessageCount:#,###,##0} messages, {FormatBytes(counters.BufferMessageBytes)}");
                    }

                    Console.Write(
                        $"; max working {FormatBytes(counters.BufferMaxOutstandingBytes)}; {counters.BufferPinCount:#,###,##0} pins; {counters.BufferLeakCount:#,###,##0} leaks");
                    Console.WriteLine();
                }

                static string FormatBytes(long bytes)
                {
                    const long K = 1024, M = K * K, G = M * K, T = G * K;

                    if (bytes < K) return $"{bytes:#,##0} B";
                    if (bytes < M) return $"{bytes / (double)K:#,##0.00} KiB";
                    if (bytes < G) return $"{bytes / (double)M:#,##0.00} MiB";
                    if (bytes < T) return $"{bytes / (double)G:#,##0.00} GiB";
                    return $"{bytes / (double)T:#,##0.00} TiB"; // I think we can stop there...
                }
            }
#endif
            if (!Quiet) Console.WriteLine();
        }
    }
}
