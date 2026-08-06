using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace AotRig;

/// <summary>
/// Minimal rig that connects and does a few things; intended to be published with
/// <c>PublishAot=true</c> so that AOT-hostile code paths (in particular the manually
/// unrolled event invocation, see issue #3157) fail loudly rather than silently.
/// </summary>
internal static class Program
{
    private static int s_failures;

    private static async Task<int> Main(string[] args)
    {
        var alive = args.Length > 0 ? args[0] : "127.0.0.1:6379";
        var dead = args.Length > 1 ? args[1] : "127.0.0.1:6390";

        Console.WriteLine($"runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"dynamic code: {System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");
        Console.WriteLine();

        await Run("connection-failed events", () => ConnectionFailedEvents(dead)).ConfigureAwait(false);
        await Run("basic operations", () => BasicOperations(alive)).ConfigureAwait(false);
        await Run("pub/sub handlers", () => PubSub(alive)).ConfigureAwait(false);
        await Run("connection events (live server)", () => LiveServerEvents(alive)).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(s_failures == 0 ? "ALL PASS" : $"{s_failures} FAILED");
        return s_failures;
    }

    private static async Task Run(string name, Func<Task> action)
    {
        Console.Write($"{name}... ");
        try
        {
            await action().ConfigureAwait(false);
            Console.WriteLine("pass");
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref s_failures);
            Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    // #3157: a failed connection raises ConnectionFailed on the thread-pool; the multiplexer
    // unrolls the multicast delegate by hand, which is where AOT bites.
    private static async Task ConnectionFailedEvents(string dead)
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 1000,
            ConnectRetry = 1,
            AllowAdmin = true,
        };
        options.EndPoints.Add(dead);

        int failed = 0, internalError = 0;
        var signal = new SemaphoreSlim(0);
        await using var muxer = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
        muxer.ConnectionFailed += (_, _) => Interlocked.Increment(ref failed);
        muxer.ConnectionFailed += (_, _) =>
        {
            // deliberately a second handler, so the multicast path is used too
            signal.Release();
        };
        muxer.InternalError += (_, _) => Interlocked.Increment(ref internalError);

        // the initial (failed) connect happens before we can attach handlers, so keep poking it:
        // each attempt is another connect failure, and therefore another event
        var db = muxer.GetDatabase();
        var deadline = Environment.TickCount64 + 30_000;
        while (!await signal.WaitAsync(250).ConfigureAwait(false))
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"no ConnectionFailed event within timeout (single-handler count: {Volatile.Read(ref failed)}, internal errors: {Volatile.Read(ref internalError)})");
            }

            try
            {
                await db.PingAsync().ConfigureAwait(false);
            }
            catch (RedisConnectionException)
            {
                // expected
            }
        }

        Console.Write($"[failed:{Volatile.Read(ref failed)} internal:{Volatile.Read(ref internalError)}] ");
    }

    private static async Task BasicOperations(string alive)
    {
        var options = ConfigurationOptions.Parse(alive);
        options.AllowAdmin = true;
        await using var muxer = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
        var db = muxer.GetDatabase();

        RedisKey key = "aot-rig:value";
        await db.KeyDeleteAsync(key).ConfigureAwait(false);
        await db.StringSetAsync(key, "hello").ConfigureAwait(false);
        var val = await db.StringGetAsync(key).ConfigureAwait(false);
        if (val != "hello") throw new InvalidOperationException($"unexpected value: {val}");

        RedisKey counter = "aot-rig:counter";
        await db.KeyDeleteAsync(counter).ConfigureAwait(false);
        if (await db.StringIncrementAsync(counter, 4).ConfigureAwait(false) != 4) throw new InvalidOperationException("INCRBY");

        RedisKey hash = "aot-rig:hash";
        await db.KeyDeleteAsync(hash).ConfigureAwait(false);
        await db.HashSetAsync(hash, [new HashEntry("a", 1), new HashEntry("b", 2)]).ConfigureAwait(false);
        if ((await db.HashGetAllAsync(hash).ConfigureAwait(false)).Length != 2) throw new InvalidOperationException("HGETALL");

        var latency = await db.PingAsync().ConfigureAwait(false);
        Console.Write($"[ping:{latency.TotalMilliseconds:0.##}ms] ");

        var server = muxer.GetServer(muxer.GetEndPoints()[0]);
        _ = server.Version;
        await db.KeyDeleteAsync([key, counter, hash]).ConfigureAwait(false);
    }

    private static async Task PubSub(string alive)
    {
        await using var muxer = await ConnectionMultiplexer.ConnectAsync(alive).ConfigureAwait(false);
        var sub = muxer.GetSubscriber();
        RedisChannel channel = RedisChannel.Literal("aot-rig:channel");

        int hits = 0;
        var signal = new SemaphoreSlim(0);
        // two handlers on the same channel: exercises the multicast unroll in the pub/sub path
        await sub.SubscribeAsync(channel, (_, _) => Interlocked.Increment(ref hits)).ConfigureAwait(false);
        await sub.SubscribeAsync(channel, (_, _) => signal.Release()).ConfigureAwait(false);

        await sub.PublishAsync(channel, "ping").ConfigureAwait(false);
        if (!await signal.WaitAsync(10_000).ConfigureAwait(false))
        {
            throw new TimeoutException($"no message within timeout (other handler: {Volatile.Read(ref hits)})");
        }

        await sub.UnsubscribeAllAsync().ConfigureAwait(false);
        Console.Write($"[hits:{Volatile.Read(ref hits)}] ");
    }

    // ConnectionRestored / ConnectionFailed against a real server, via a forced reconnect
    private static async Task LiveServerEvents(string alive)
    {
        const string ClientName = "aot-rig";
        var options = ConfigurationOptions.Parse(alive);
        options.AllowAdmin = true;
        options.AbortOnConnectFail = false;
        options.ClientName = ClientName;
        await using var muxer = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);

        int restored = 0, failed = 0;
        var signal = new SemaphoreSlim(0);
        muxer.ConnectionRestored += (_, _) => Interlocked.Increment(ref restored);
        muxer.ConnectionRestored += (_, _) => signal.Release();
        muxer.ConnectionFailed += (_, _) => Interlocked.Increment(ref failed);
        muxer.ErrorMessage += (_, _) => { };

        var db = muxer.GetDatabase();
        await db.PingAsync().ConfigureAwait(false);

        // kill our own connections server-side; the multiplexer should notice and re-establish,
        // raising ConnectionFailed/ConnectionRestored through the machinery we care about
        var server = muxer.GetServer(muxer.GetEndPoints()[0]);
        foreach (var client in await server.ClientListAsync().ConfigureAwait(false))
        {
            if (client.Name == ClientName)
            {
                try
                {
                    await server.ClientKillAsync(id: client.Id, skipMe: false).ConfigureAwait(false);
                }
                catch (RedisConnectionException)
                {
                    // expected when we kill the connection issuing the command
                }
            }
        }

        if (!await signal.WaitAsync(20_000).ConfigureAwait(false))
        {
            throw new TimeoutException($"no ConnectionRestored within timeout (failed: {Volatile.Read(ref failed)})");
        }

        await db.PingAsync().ConfigureAwait(false);
        Console.Write($"[failed:{Volatile.Read(ref failed)} restored:{Volatile.Read(ref restored)}] ");
    }
}
