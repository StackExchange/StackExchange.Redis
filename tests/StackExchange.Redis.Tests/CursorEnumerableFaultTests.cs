using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Fault handling in the scan enumerators (#3260): a page that faults after the consumer has stopped
/// waiting for it must not surface as <see cref="TaskScheduler.UnobservedTaskException"/>, and a page
/// that has already faulted must surface as its own exception rather than an <see cref="AggregateException"/>.
/// No server is involved: the pages are scripted, and the multiplexer never connects.
/// </summary>
[Collection(NonParallelCollection.Name)] // forces full GCs, and the unobserved-exception event is process-wide
public class CursorEnumerableFaultTests(ITestOutputHelper output) : TestBase(output)
{
    private const int SyncTimeout = 300;

    private static async Task<ConnectionMultiplexer> CreateDisconnectedAsync()
        => await ConnectionMultiplexer.ConnectAsync($"127.0.0.1:1,abortConnect=false,connectTimeout=500,connectRetry=0,syncTimeout={SyncTimeout}");

    private static void ForceGC()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
        }
    }

    private static CursorEnumerable<HashEntry>.ScanResult Page(long nextCursor, params HashEntry[] entries)
        => new(nextCursor, entries, entries.Length, isPooled: false);

    [Fact]
    public async Task SyncTimeout_LateFault_IsObserved()
    {
        using var conn = await CreateDisconnectedAsync();
        var db = new ScriptedDatabase(conn);
        var marker = new MarkerException();
        using var tracker = new UnobservedTracker(marker);

        // the first page never answers inside the sync timeout...
        var page = new TaskCompletionSource<CursorEnumerable<HashEntry>.ScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Enqueue(page.Task);

        using (var iter = db.Api.HashScan("key", "*", pageSize: 1).GetEnumerator())
        {
            var ex = Assert.Throws<RedisTimeoutException>(() => iter.MoveNext());
            Log(ex.Message);
        }

        // ...and faults later, when nothing on the caller's side is waiting for it any more
        page.SetException(marker);
        await WaitForFaultToPropagateAsync(marker);
        ForceGC();

        Assert.Equal(1, db.Requests);
        Assert.False(tracker.Seen, "the late fault surfaced as an unobserved task exception");
    }

    [Fact]
    public async Task Dispose_WithPrefetchInFlight_LateFault_IsObserved()
    {
        using var conn = await CreateDisconnectedAsync();
        var db = new ScriptedDatabase(conn);
        var marker = new MarkerException();
        using var tracker = new UnobservedTracker(marker);

        // page one answers at once with a non-terminal cursor, so page two is fetched ahead; page two never answers
        db.Enqueue(Task.FromResult(Page(42, new HashEntry("f", "v"))));
        var page2 = new TaskCompletionSource<CursorEnumerable<HashEntry>.ScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // the task handed to the enumerator must not be one the test itself keeps alive, or it can never be
        // finalized; relay it through a task that only the enumerator holds
        var relayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Enqueue(Relay(page2.Task, relayed));

        int count = 0;
        foreach (var entry in db.Api.HashScan("key", "*", pageSize: 1))
        {
            count++;
            break; // disposes the enumerator with the prefetch still in flight
        }
        Assert.Equal(1, count);
        Assert.Equal(2, db.Requests);

        page2.SetException(marker);
        await relayed.Task;
        await Task.Delay(50); // let the relay finish faulting its task
        ForceGC();

        Assert.False(tracker.Seen, "the abandoned prefetch surfaced as an unobserved task exception");
    }

    private static async Task<T> Relay<T>(Task<T> source, TaskCompletionSource<bool> faulted)
    {
        try
        {
            return await source;
        }
        catch
        {
            faulted.TrySetResult(true);
            throw;
        }
    }

    [Fact]
    public async Task SyncTimeout_WithoutPattern_IsTimeoutException()
    {
        // without a pattern the scan is a single HGETALL etc, and the enumerator has no message to report on
        using var conn = await CreateDisconnectedAsync();
        var db = (RedisDatabase)conn.GetDatabase();
        var page = new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var iter = CursorEnumerable<int>.From(db, null, page.Task, 0).GetEnumerator();
        var ex = Assert.Throws<RedisTimeoutException>(() => iter.MoveNext());
        Log(ex.Message);
        page.SetCanceled();
    }

    [Fact]
    public async Task FaultedPage_Sync_ThrowsCause()
    {
        using var conn = await CreateDisconnectedAsync();
        var db = new ScriptedDatabase(conn);
        var marker = new MarkerException();

        db.Enqueue(Task.FromException<CursorEnumerable<HashEntry>.ScanResult>(marker));
        using var iter = db.Api.HashScan("key", "*", pageSize: 1).GetEnumerator();
        var ex = Assert.Throws<MarkerException>(() => iter.MoveNext());
        Assert.Same(marker, ex);
    }

    [Fact]
    public async Task FaultedPage_Async_ThrowsCause()
    {
        using var conn = await CreateDisconnectedAsync();
        var db = new ScriptedDatabase(conn);
        var marker = new MarkerException();

        db.Enqueue(Task.FromException<CursorEnumerable<HashEntry>.ScanResult>(marker));
        await using var iter = db.Api.HashScanAsync("key", "*", pageSize: 1).GetAsyncEnumerator();
        var ex = await Assert.ThrowsAsync<MarkerException>(async () => await iter.MoveNextAsync());
        Assert.Same(marker, ex);
    }

    [Fact]
    public async Task LateFaultedPage_Sync_ThrowsCause()
    {
        using var conn = await CreateDisconnectedAsync();
        var db = new ScriptedDatabase(conn);
        var marker = new MarkerException();

        var page = new TaskCompletionSource<CursorEnumerable<HashEntry>.ScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Enqueue(page.Task);
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            page.SetException(marker);
        });
        using var iter = db.Api.HashScan("key", "*", pageSize: 1).GetEnumerator();
        var ex = Assert.Throws<MarkerException>(() => iter.MoveNext());
        Assert.Same(marker, ex);
        Assert.True(ex.Data.Contains("Redis-page-size"));
    }

    /// <summary>
    /// The enumerator stamps page details onto the exception as it rethrows it, which is the only
    /// externally visible sign that the (now unobserved) internal task has faulted.
    /// </summary>
    private static async Task WaitForFaultToPropagateAsync(Exception ex)
    {
        for (int i = 0; i < 100 && !ex.Data.Contains("Redis-page-size"); i++)
        {
            await Task.Delay(20);
        }
        Assert.True(ex.Data.Contains("Redis-page-size"), "the fault never reached the enumerator");
        await Task.Delay(50); // let the state machine finish faulting its task
    }

    private sealed class MarkerException : Exception
    {
        public MarkerException() : base("scripted page fault") { }
    }

    private sealed class UnobservedTracker : IDisposable
    {
        private readonly Exception _marker;
        private int _seen;
        public bool Seen => Volatile.Read(ref _seen) != 0;

        public UnobservedTracker(Exception marker)
        {
            _marker = marker;
            TaskScheduler.UnobservedTaskException += OnUnobserved;
        }

        private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.Flatten().InnerExceptions.Any(x => ReferenceEquals(x, _marker)))
            {
                Interlocked.Exchange(ref _seen, 1);
                e.SetObserved();
            }
        }

        public void Dispose() => TaskScheduler.UnobservedTaskException -= OnUnobserved;
    }

    /// <summary>
    /// A database whose scan pages come from a script rather than a server.
    /// </summary>
    private sealed class ScriptedDatabase(ConnectionMultiplexer muxer) : RedisDatabase(muxer, 0, null)
    {
        private readonly Queue<Task> _pages = new();
        public int Requests { get; private set; }
        public IDatabase Api => this;

        public void Enqueue<T>(Task<T> page) => _pages.Enqueue(page);

        internal override RedisFeatures GetFeatures(in RedisKey key, CommandFlags flags, RedisCommand command, out ServerEndPoint? server)
        {
            server = null;
            return new RedisFeatures(RedisFeatures.v2_8_0);
        }

        internal override Task<T?> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null) where T : default
        {
            Requests++;
            Assert.NotNull(message);
            return (Task<T?>)_pages.Dequeue();
        }
    }
}
