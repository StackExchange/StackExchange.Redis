﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class PreserveOrder : TestBase
    {
        public PreserveOrder(ITestOutputHelper output) : base (output) { }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Execute(bool preserveAsyncOrder)
        {
            using (var conn = Create())
            {
                var sub = conn.GetSubscriber();
                var received = new List<int>();
                Output.WriteLine("Subscribing...");
                const int COUNT = 1000;
                sub.Subscribe("foo", (channel, message) =>
                {
                    lock (received)
                    {
                        received.Add((int)message);
                        if (received.Count == COUNT)
                            Monitor.PulseAll(received); // wake the test rig
                    }
                    Thread.Sleep(1); // you kinda need to be slow, otherwise
                    // the pool will end up doing everything on one thread
                });
                conn.PreserveAsyncOrder = preserveAsyncOrder;
                Output.WriteLine("");
                Output.WriteLine("Sending ({0})...", preserveAsyncOrder ? "preserved order" : "any order");
                lock (received)
                {
                    received.Clear();
                    // we'll also use received as a wait-detection mechanism; sneaky

                    // note: this does not do any cheating;
                    // it all goes to the server and back
                    for (int i = 0; i < COUNT; i++)
                    {
                        sub.Publish("foo", i);
                    }

                    Output.WriteLine("Allowing time for delivery etc...");
                    var watch = Stopwatch.StartNew();
                    if (!Monitor.Wait(received, 10000))
                    {
                        Output.WriteLine("Timed out; expect less data");
                    }
                    watch.Stop();
                    Output.WriteLine("Checking...");
                    lock (received)
                    {
                        Output.WriteLine("Received: {0} in {1}ms", received.Count, watch.ElapsedMilliseconds);
                        int wrongOrder = 0;
                        for (int i = 0; i < Math.Min(COUNT, received.Count); i++)
                        {
                            if (received[i] != i) wrongOrder++;
                        }
                        Output.WriteLine("Out of order: " + wrongOrder);
                        if (preserveAsyncOrder) Assert.Equal(0, wrongOrder);
                        else Assert.NotEqual(0, wrongOrder);
                    }
                }
            }
        }
    }
}