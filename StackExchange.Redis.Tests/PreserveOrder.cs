using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class PreserveOrder : TestBase
    {
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void Execute(bool preserveAsyncOrder)
        {
            using (var conn = Create())
            {
                var sub = conn.GetSubscriber();
                var received = new List<int>();
                Console.WriteLine("Subscribing...");
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
                Console.WriteLine();
                Console.WriteLine("Sending ({0})...", (preserveAsyncOrder ? "preserved order" : "any order"));
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

                    Console.WriteLine("Allowing time for delivery etc...");
                    var watch = Stopwatch.StartNew();
                    if (!Monitor.Wait(received, 10000))
                    {
                        Console.WriteLine("Timed out; expect less data");
                    }
                    watch.Stop();
                    Console.WriteLine("Checking...");
                    lock (received)
                    {
                        Console.WriteLine("Received: {0} in {1}ms", received.Count, watch.ElapsedMilliseconds);
                        int wrongOrder = 0;
                        for (int i = 0; i < Math.Min(COUNT, received.Count); i++)
                        {
                            if (received[i] != i) wrongOrder++;
                        }
                        Console.WriteLine("Out of order: " + wrongOrder);
                        if (preserveAsyncOrder) Assert.AreEqual(0, wrongOrder);
                        else Assert.AreNotEqual(0, wrongOrder);
                    }
                }
            }
        }
    }
}
