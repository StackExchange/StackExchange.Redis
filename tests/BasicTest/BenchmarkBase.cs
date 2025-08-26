using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Resp;
using Void = Resp.Void;

// influenced by redis-benchmark, see .md file
namespace BasicTest;

public abstract class BenchmarkBase<TClient> : IDisposable
{
    protected const string
        _getSetKey = "key:__rand_int__",
        _counterKey = "counter:__rand_int__",
        _listKey = "mylist",
        _setKey = "myset",
        _hashKey = "myhash",
        _sortedSetKey = "myzset",
        _streamKey = "mystream";

    private readonly HashSet<string> _tests = new(StringComparer.OrdinalIgnoreCase);
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

    protected virtual Task OnCleanupAsync(TClient client) => Task.CompletedTask;

    protected virtual Task InitAsync(TClient client) => Task.CompletedTask;

    public async Task InitAsync()
    {
        for (int i = 0; i < ClientCount; i++)
        {
            await InitAsync(GetClient(i)).ConfigureAwait(false);
        }
    }

    protected Task<Void> Pipeline(Func<Task> operation) => Pipeline(() => new ValueTask(operation()));
    protected Task<T> Pipeline<T>(Func<Task<T>> operation) => Pipeline(() => new ValueTask<T>(operation()));
    protected async Task<Void> Pipeline(Func<ValueTask> operation)
    {
        var opsPerClient = OperationsPerClient;
        int i = 0;
        try
        {
            if (PipelineDepth == 1)
            {
                for (; i < opsPerClient; i++)
                {
                    await operation().ConfigureAwait(false);
                }
            }
            else
            {
                var queue = new Queue<ValueTask>(opsPerClient);
                for (; i < opsPerClient; i++)
                {
                    if (queue.Count == opsPerClient)
                    {
                        await queue.Dequeue().ConfigureAwait(false);
                    }

                    queue.Enqueue(operation());
                }

                while (queue.Count > 0)
                {
                    await queue.Dequeue().ConfigureAwait(false);
                }
            }

            return Void.Instance;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{operation.Method.Name} failed after {i} operations: {ex.Message}",
                ex);
        }
    }

    protected async Task<T> Pipeline<T>(Func<ValueTask<T>> operation)
    {
        var opsPerClient = OperationsPerClient;
        int i = 0;
        try
        {
            T result = default;
            if (PipelineDepth == 1)
            {
                for (; i < opsPerClient; i++)
                {
                    result = await operation().ConfigureAwait(false);
                }
            }
            else
            {
                var queue = new Queue<ValueTask<T>>(opsPerClient);
                for (; i < opsPerClient; i++)
                {
                    if (queue.Count == opsPerClient)
                    {
                        _ = await queue.Dequeue().ConfigureAwait(false);
                    }

                    queue.Enqueue(operation());
                }

                while (queue.Count > 0)
                {
                    result = await queue.Dequeue().ConfigureAwait(false);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{operation.Method.Name} failed after {i} operations: {ex.Message}",
                ex);
        }
    }

    protected abstract TClient GetClient(int index);
    protected virtual TClient WithCancellation(TClient client, CancellationToken cancellationToken) => client;
    protected abstract Task Delete(TClient client, string key);

    protected async Task RunAsync<T>(
        string key,
        Func<TClient, Task<T>> action,
        Func<TClient, Task> init = null,
        string format = "")
    {
        string name = action.Method.Name;

        if (action.Method.GetCustomAttribute(typeof(DisplayNameAttribute)) is DisplayNameAttribute
            {
                DisplayName: { Length: > 0 }
            } dna)
        {
            name = dna.DisplayName;
        }

        // skip test if not needed
        if (_tests.Count != 0 && !_tests.Contains(name)) return;

        // include additional test metadata
        string description = "";
        if (action.Method.GetCustomAttribute(typeof(DescriptionAttribute)) is DescriptionAttribute
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

            if (PipelineDepth > 1)
            {
                Console.Write($", pipeline: {PipelineDepth:#,##0}");
            }

            Console.WriteLine(")");
        }

        try
        {
            if (key is not null)
            {
                await Delete(GetClient(0), key).ConfigureAwait(false);
            }

            if (init is not null)
            {
                await init(GetClient(0)).ConfigureAwait(false);
            }

            var pending = new Task<T>[ClientCount];
            int index = 0;
#if DEBUG
            DebugCounters.Flush();
#endif
            // optionally support cancellation, applied per-test
            CancellationToken cancellationToken = CancellationToken.None;
            using var cts = SupportCancel ? new CancellationTokenSource(TimeSpan.FromSeconds(20)) : null;
            if (SupportCancel) cancellationToken = cts!.Token;

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < ClientCount; i++)
            {
                var client = GetClient(i);
                pending[index++] = Task.Run(() => action(WithCancellation(client, cancellationToken)));
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

            if (typeof(T) != typeof(Void) && !Quiet)
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
            Console.Error.WriteLine(ex.Message);
        }
        finally
        {
#if DEBUG
            var counters = DebugCounters.Flush(); // flush even if not showing
            if (Quiet)
            {
                if (counters.WriteBytes != 0)
                {
                    Console.Write($"Write: {FormatBytes(counters.WriteBytes)}");
                    if (counters.WriteCount != 0) Console.Write($"; {counters.WriteCount:#,##0} sync");
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

                if (counters.DiscardAverage + counters.DiscardPartialCount != 0)
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

                static string FormatBytes(long bytes)
                {
                    if (bytes > 1024 * 1024)
                    {
                        return $"{bytes >> 20:#,##0} MiB";
                    }

                    if (bytes > 1024)
                    {
                        return $"{bytes >> 10:#,##0} KiB";
                    }

                    return $"{bytes} B";
                }
            }
#endif
            if (!Quiet) Console.WriteLine();
        }
    }
}
