using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class TaskManagerTests
    {
        [Test]
        public void TestExecuteNormal()
        {
            var ids = new List<int>();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            Trace.WriteLine("0) Thread: " + threadId);
            var result = TaskManager.Execute(async () =>
            {
                Trace.WriteLine("1) Thread: " + Thread.CurrentThread.ManagedThreadId);
                ids.Add(Thread.CurrentThread.ManagedThreadId);
                await Task.Yield();
                Trace.WriteLine("2) Thread: " + Thread.CurrentThread.ManagedThreadId);
                ids.Add(Thread.CurrentThread.ManagedThreadId);
                await Task.Delay(50).ForAwait();
                Trace.WriteLine("3) Thread: " + Thread.CurrentThread.ManagedThreadId);
                ids.Add(Thread.CurrentThread.ManagedThreadId);
                Task t1 = Subtask(4, 1), t2 = Subtask(4, 2), t3 = Subtask(4, 3);
                await Task.WhenAll(t1, t2, t3);
                Trace.WriteLine("5) Thread: " + Thread.CurrentThread.ManagedThreadId);
            }, 2000);
            Assert.AreEqual(true, result);
            CollectionAssert.AreEqual(ids, Enumerable.Repeat(threadId, 3).ToList());
        }

        static async Task Subtask(int step, int subtask)
        {
            await Task.Delay(30);
            Trace.WriteLine(string.Format("{0}) Thread: {1} (Subtask {2})", step, Thread.CurrentThread.ManagedThreadId, subtask));
        }

        [Test]
        public void TestExecuteTimeout()
        {
            int id1 = -1, id2 = -1;
            var threadId = Thread.CurrentThread.ManagedThreadId;
            Trace.WriteLine("0) Thread: " + threadId);
            using (var handle = new ManualResetEvent(false))
            {
                var result = TaskManager.Execute(async () =>
                {
                    Trace.WriteLine("1) Thread: " + Thread.CurrentThread.ManagedThreadId);
                    id1 = Thread.CurrentThread.ManagedThreadId;
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    Trace.WriteLine("2) Thread: " + Thread.CurrentThread.ManagedThreadId);
                    id2 = Thread.CurrentThread.ManagedThreadId;
                    handle.Set();
                }, 100);
                Assert.AreEqual(false, result);
                Assert.AreEqual(true, handle.WaitOne(5000));
                Assert.AreEqual(threadId, id1);
                Assert.AreNotEqual(threadId, id2);
            }
        }
    }
}
