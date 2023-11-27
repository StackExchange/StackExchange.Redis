using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class PreserveOrderTests : TestBase
{
    public PreserveOrderTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base (output, fixture) { }

    [Fact]
    public void Execute()
    {
        using var conn = Create();

        var sub = conn.GetSubscriber();
        var channel = Me();
        var received = new List<int>();
        Log("Subscribing...");
        const int COUNT = 500;
        sub.Subscribe(RedisChannel.Literal(channel), (_, message) =>
        {
            lock (received)
            {
                received.Add((int)message);
                if (received.Count == COUNT)
                    Monitor.PulseAll(received); // wake the test rig
                }
        });
        Log("");
        Log("Sending (any order)...");
        lock (received)
        {
            received.Clear();
            // we'll also use received as a wait-detection mechanism; sneaky

            // note: this does not do any cheating;
            // it all goes to the server and back
            for (int i = 0; i < COUNT; i++)
            {
                sub.Publish(RedisChannel.Literal(channel), i, CommandFlags.FireAndForget);
            }

            Log("Allowing time for delivery etc...");
            var watch = Stopwatch.StartNew();
            if (!Monitor.Wait(received, 10000))
            {
                Log("Timed out; expect less data");
            }
            watch.Stop();
            Log("Checking...");
            lock (received)
            {
                Log("Received: {0} in {1}ms", received.Count, watch.ElapsedMilliseconds);
                int wrongOrder = 0;
                for (int i = 0; i < Math.Min(COUNT, received.Count); i++)
                {
                    if (received[i] != i) wrongOrder++;
                }
                Log("Out of order: " + wrongOrder);
            }
        }
    }
}
