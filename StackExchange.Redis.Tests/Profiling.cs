using System;
using System.Collections.Generic;
using System.Linq;
#if CORE_CLR
using System.Reflection;
#endif
using System.Threading.Tasks;
using NUnit.Framework;
using System.Threading;
using System.Collections.Concurrent;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Profiling : TestBase
    {
        class TestProfiler : IProfiler
        {
            public object MyContext = new object();

            public object GetContext()
            {
                return MyContext;
            }
        }

        [Test]
        public void Simple()
        {
            using (var conn = Create())
            {
                var profiler = new TestProfiler();

                conn.RegisterProfiler(profiler);
                conn.BeginProfiling(profiler.MyContext);
                var db = conn.GetDatabase(4);
                db.StringSet("hello", "world");
                var val = db.StringGet("hello");
                Assert.AreEqual("world", (string)val);

                var cmds = conn.FinishProfiling(profiler.MyContext);
                Assert.AreEqual(2, cmds.Count());

                var set = cmds.SingleOrDefault(cmd => cmd.Command == "SET");
                Assert.IsNotNull(set);
                var get = cmds.SingleOrDefault(cmd => cmd.Command == "GET");
                Assert.IsNotNull(get);

                Assert.IsTrue(set.CommandCreated <= get.CommandCreated);

                Assert.AreEqual(4, set.Db);
                Assert.AreEqual(conn.GetEndPoints()[0], set.EndPoint);
                Assert.IsTrue(set.CreationToEnqueued > TimeSpan.Zero);
                Assert.IsTrue(set.EnqueuedToSending > TimeSpan.Zero);
                Assert.IsTrue(set.SentToResponse > TimeSpan.Zero);
                Assert.IsTrue(set.ResponseToCompletion > TimeSpan.Zero);
                Assert.IsTrue(set.ElapsedTime > TimeSpan.Zero);
                Assert.IsTrue(set.ElapsedTime > set.CreationToEnqueued && set.ElapsedTime > set.EnqueuedToSending && set.ElapsedTime > set.SentToResponse);
                Assert.IsTrue(set.RetransmissionOf == null);
                Assert.IsTrue(set.RetransmissionReason == null);

                Assert.AreEqual(4, get.Db);
                Assert.AreEqual(conn.GetEndPoints()[0], get.EndPoint);
                Assert.IsTrue(get.CreationToEnqueued > TimeSpan.Zero);
                Assert.IsTrue(get.EnqueuedToSending > TimeSpan.Zero);
                Assert.IsTrue(get.SentToResponse > TimeSpan.Zero);
                Assert.IsTrue(get.ResponseToCompletion > TimeSpan.Zero);
                Assert.IsTrue(get.ElapsedTime > TimeSpan.Zero);
                Assert.IsTrue(get.ElapsedTime > get.CreationToEnqueued && get.ElapsedTime > get.EnqueuedToSending && get.ElapsedTime > get.SentToResponse);
                Assert.IsTrue(get.RetransmissionOf == null);
                Assert.IsTrue(get.RetransmissionReason == null);
            }
        }

        [Test]
        public void ManyThreads()
        {
            using (var conn = Create())
            {
                var profiler = new TestProfiler();

                conn.RegisterProfiler(profiler);
                conn.BeginProfiling(profiler.MyContext);

                var threads = new List<Thread>();

                for (var i = 0; i < 16; i++)
                {
                    var db = conn.GetDatabase(i);

                    threads.Add(
                        new Thread(
                            delegate()
                            {
                                var threadTasks = new List<Task>();

                                for (var j = 0; j < 1000; j++)
                                {
                                    var task = db.StringSetAsync("" + j, "" + j);
                                    threadTasks.Add(task);
                                }

                                Task.WaitAll(threadTasks.ToArray());
                            }
                        )
                    );
                }

                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());

                var allVals = conn.FinishProfiling(profiler.MyContext);

                var kinds = allVals.Select(cmd => cmd.Command).Distinct().ToList();
                Assert.IsTrue(kinds.Count <= 2);
                Assert.IsTrue(kinds.Contains("SET"));
                if (kinds.Count == 2 && !kinds.Contains("SELECT"))
                {
                    Assert.Fail("Non-SET, Non-SELECT command seen");
                }

                Assert.AreEqual(16 * 1000, allVals.Count());
                Assert.AreEqual(16, allVals.Select(cmd => cmd.Db).Distinct().Count());

                for (var i = 0; i < 16; i++)
                {
                    var setsInDb = allVals.Where(cmd => cmd.Db == i && cmd.Command == "SET").Count();
                    Assert.AreEqual(1000, setsInDb);
                }
            }
        }

        class TestProfiler2 : IProfiler
        {
            ConcurrentDictionary<int, object> Contexts = new ConcurrentDictionary<int, object>();

            public void RegisterContext(object context)
            {
                Contexts[Thread.CurrentThread.ManagedThreadId] = context;
            }

            public object GetContext()
            {
                object ret;
                if (!Contexts.TryGetValue(Thread.CurrentThread.ManagedThreadId, out ret)) ret = null;

                return ret;
            }
        }

        [Test]
        public void ManyContexts()
        {
            using (var conn = Create())
            {
                var profiler = new TestProfiler2();
                conn.RegisterProfiler(profiler);

                var perThreadContexts = new List<object>();
                for (var i = 0; i < 16; i++)
                {
                    perThreadContexts.Add(new object());
                }

                var threads = new List<Thread>();

                var results = new IEnumerable<IProfiledCommand>[16];

                for (var i = 0; i < 16; i++)
                {
                    var ix = i;
                    var thread =
                        new Thread(
                            delegate()
                            {
                                var ctx = perThreadContexts[ix];
                                profiler.RegisterContext(ctx);

                                conn.BeginProfiling(ctx);
                                var db = conn.GetDatabase(ix);

                                var allTasks = new List<Task>();

                                for (var j = 0; j < 1000; j++)
                                {
                                    allTasks.Add(db.StringGetAsync("hello" + ix));
                                    allTasks.Add(db.StringSetAsync("hello" + ix, "world" + ix));
                                }

                                Task.WaitAll(allTasks.ToArray());

                                results[ix] = conn.FinishProfiling(ctx);
                            }
                        );

                    threads.Add(thread);
                }

                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());

                for (var i = 0; i < results.Length; i++)
                {
                    var res = results[i];
                    Assert.IsNotNull(res);

                    var numGets = res.Count(r => r.Command == "GET");
                    var numSets = res.Count(r => r.Command == "SET");

                    Assert.AreEqual(1000, numGets);
                    Assert.AreEqual(1000, numSets);
                    Assert.IsTrue(res.All(cmd => cmd.Db == i));
                }
            }
        }

        class TestProfiler3 : IProfiler
        {
            ConcurrentDictionary<int, object> Contexts = new ConcurrentDictionary<int, object>();

            public void RegisterContext(object context)
            {
                Contexts[Thread.CurrentThread.ManagedThreadId] = context;
            }

            public object AnyContext()
            {
                return Contexts.First().Value;
            }

            public void Reset()
            {
                Contexts.Clear();
            }

            public object GetContext()
            {
                object ret;
                if (!Contexts.TryGetValue(Thread.CurrentThread.ManagedThreadId, out ret)) ret = null;

                return ret;
            }
        }

        // This is a separate method for target=DEBUG purposes.
        // In release builds, the runtime is smart enough to figure out
        //   that the contexts are unreachable and should be collected but in
        //   debug builds... well, it's not very smart.
        object LeaksCollectedAndRePooled_Initialize(ConnectionMultiplexer conn, int threadCount)
        {
            var profiler = new TestProfiler3();
            conn.RegisterProfiler(profiler);

            var perThreadContexts = new List<object>();
            for (var i = 0; i < threadCount; i++)
            {
                perThreadContexts.Add(new object());
            }

            var threads = new List<Thread>();

            var results = new IEnumerable<IProfiledCommand>[threadCount];

            for (var i = 0; i < threadCount; i++)
            {
                var ix = i;
                var thread =
                    new Thread(
                        delegate()
                        {
                            var ctx = perThreadContexts[ix];
                            profiler.RegisterContext(ctx);

                            conn.BeginProfiling(ctx);
                            var db = conn.GetDatabase(ix);

                            var allTasks = new List<Task>();

                            for (var j = 0; j < 1000; j++)
                            {
                                allTasks.Add(db.StringGetAsync("hello" + ix));
                                allTasks.Add(db.StringSetAsync("hello" + ix, "world" + ix));
                            }

                            Task.WaitAll(allTasks.ToArray());

                            // intentionally leaking!
                        }
                    );

                threads.Add(thread);
            }

            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            var anyContext = profiler.AnyContext();
            profiler.Reset();

            return anyContext;
        }

        [Test]
        public void LeaksCollectedAndRePooled()
        {
            const int ThreadCount = 16;

            using (var conn = Create())
            {
                var anyContext = LeaksCollectedAndRePooled_Initialize(conn, ThreadCount);

                // force collection of everything but `anyContext`
                GC.Collect(3, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();

                Thread.Sleep(TimeSpan.FromMinutes(1.01));
                conn.FinishProfiling(anyContext);

                // make sure we haven't left anything in the active contexts dictionary
                Assert.AreEqual(0, conn.profiledCommands.ContextCount);
                Assert.AreEqual(ThreadCount, ConcurrentProfileStorageCollection.AllocationCount);
                Assert.AreEqual(ThreadCount, ConcurrentProfileStorageCollection.CountInPool());
            }
        }

        [Test]
        public void ReuseStorage()
        {
            const int ThreadCount = 16;

            // have to reset so other tests don't clober
            ConcurrentProfileStorageCollection.AllocationCount = 0;

            using (var conn = Create())
            {
                var profiler = new TestProfiler2();
                conn.RegisterProfiler(profiler);

                var perThreadContexts = new List<object>();
                for (var i = 0; i < 16; i++)
                {
                    perThreadContexts.Add(new object());
                }

                var threads = new List<Thread>();

                var results = new List<IEnumerable<IProfiledCommand>>[16];
                for (var i = 0; i < 16; i++)
                {
                    results[i] = new List<IEnumerable<IProfiledCommand>>();
                }

                for (var i = 0; i < ThreadCount; i++)
                {
                    var ix = i;
                    var thread =
                        new Thread(
                            delegate()
                            {
                                for (var k = 0; k < 10; k++)
                                {
                                    var ctx = perThreadContexts[ix];
                                    profiler.RegisterContext(ctx);

                                    conn.BeginProfiling(ctx);
                                    var db = conn.GetDatabase(ix);

                                    var allTasks = new List<Task>();

                                    for (var j = 0; j < 1000; j++)
                                    {
                                        allTasks.Add(db.StringGetAsync("hello" + ix));
                                        allTasks.Add(db.StringSetAsync("hello" + ix, "world" + ix));
                                    }

                                    Task.WaitAll(allTasks.ToArray());

                                    results[ix].Add(conn.FinishProfiling(ctx));
                                }
                            }
                        );

                    threads.Add(thread);
                }

                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());

                // only 16 allocations can ever be in flight at once
                var allocCount = ConcurrentProfileStorageCollection.AllocationCount;
                Assert.IsTrue(allocCount <= ThreadCount, allocCount.ToString());

                // correctness check for all allocations
                for (var i = 0; i < results.Length; i++)
                {
                    var resList = results[i];
                    foreach (var res in resList)
                    {
                        Assert.IsNotNull(res);

                        var numGets = res.Count(r => r.Command == "GET");
                        var numSets = res.Count(r => r.Command == "SET");

                        Assert.AreEqual(1000, numGets);
                        Assert.AreEqual(1000, numSets);
                        Assert.IsTrue(res.All(cmd => cmd.Db == i));
                    }
                }

                // no crossed streams
                var everything = results.SelectMany(r => r).ToList();
                for (var i = 0; i < everything.Count; i++)
                {
                    for (var j = 0; j < everything.Count; j++)
                    {
                        if (i == j) continue;

                        if (object.ReferenceEquals(everything[i], everything[j]))
                        {
                            Assert.Fail("Profilings were jumbled");
                        }
                    }
                }
            }
        }

        [Test]
        public void LowAllocationEnumerable()
        {
            const int OuterLoop = 10000;

            using(var conn = Create())
            {
                var profiler = new TestProfiler();
                conn.RegisterProfiler(profiler);

                conn.BeginProfiling(profiler.MyContext);

                var db = conn.GetDatabase();

                var allTasks = new List<Task<string>>();

                foreach (var i in Enumerable.Range(0, OuterLoop))
                {
                    var t =
                        db.StringSetAsync("foo" + i, "bar" + i)
                          .ContinueWith(
                            async _ =>
                            {
                                return (string)(await db.StringGetAsync("foo" + i).ConfigureAwait(false));
                            }
                          );

                    var finalResult = t.Unwrap();
                    allTasks.Add(finalResult);
                }

                conn.WaitAll(allTasks.ToArray());

                var res = conn.FinishProfiling(profiler.MyContext);
                Assert.IsTrue(res.GetType().GetTypeInfo().IsValueType);

                using(var e = res.GetEnumerator())
                {
                    Assert.IsTrue(e.GetType().GetTypeInfo().IsValueType);

                    Assert.IsTrue(e.MoveNext());
                    var i = e.Current;

                    e.Reset();
                    Assert.IsTrue(e.MoveNext());
                    var j = e.Current;

                    Assert.IsTrue(object.ReferenceEquals(i, j));
                }

                Assert.AreEqual(OuterLoop * 2, res.Count());
                Assert.AreEqual(OuterLoop, res.Count(r => r.Command == "GET"));
                Assert.AreEqual(OuterLoop, res.Count(r => r.Command == "SET"));
            }
        }

        class ToyProfiler : IProfiler
        {
            public ConcurrentDictionary<Thread, object> Contexts = new ConcurrentDictionary<Thread, object>();

            public object GetContext()
            {
                object ctx;
                if (!Contexts.TryGetValue(Thread.CurrentThread, out ctx)) ctx = null;

                return ctx;
            }
        }

        [Test]
        public void ProfilingMD_Ex1()
        {
            using (var c = Create())
            {
                ConnectionMultiplexer conn = c;
                var profiler = new ToyProfiler();
                var thisGroupContext = new object();

                conn.RegisterProfiler(profiler);

                var threads = new List<Thread>();

                for (var i = 0; i < 16; i++)
                {
                    var db = conn.GetDatabase(i);

                    var thread =
                        new Thread(
                            delegate()
                            {
                                var threadTasks = new List<Task>();

                                for (var j = 0; j < 1000; j++)
                                {
                                    var task = db.StringSetAsync("" + j, "" + j);
                                    threadTasks.Add(task);
                                }

                                Task.WaitAll(threadTasks.ToArray());
                            }
                        );

                    profiler.Contexts[thread] = thisGroupContext;

                    threads.Add(thread);
                }

                conn.BeginProfiling(thisGroupContext);

                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());

                IEnumerable<IProfiledCommand> timings = conn.FinishProfiling(thisGroupContext);

                Assert.AreEqual(16000, timings.Count());
            }
        }

        [Test]
        public void ProfilingMD_Ex2()
        {
            using (var c = Create())
            {
                ConnectionMultiplexer conn = c;
                var profiler = new ToyProfiler();

                conn.RegisterProfiler(profiler);

                var threads = new List<Thread>();

                var perThreadTimings = new ConcurrentDictionary<Thread, List<IProfiledCommand>>();

                for (var i = 0; i < 16; i++)
                {
                    var db = conn.GetDatabase(i);

                    var thread =
                        new Thread(
                            delegate()
                            {
                                var threadTasks = new List<Task>();

                                conn.BeginProfiling(Thread.CurrentThread);

                                for (var j = 0; j < 1000; j++)
                                {
                                    var task = db.StringSetAsync("" + j, "" + j);
                                    threadTasks.Add(task);
                                }

                                Task.WaitAll(threadTasks.ToArray());

                                perThreadTimings[Thread.CurrentThread] = conn.FinishProfiling(Thread.CurrentThread).ToList();
                            }
                        );

                    profiler.Contexts[thread] = thread;

                    threads.Add(thread);
                }
                
                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());

                Assert.AreEqual(16, perThreadTimings.Count);
                Assert.IsTrue(perThreadTimings.All(kv => kv.Value.Count == 1000));
            }
        }
    }
}
