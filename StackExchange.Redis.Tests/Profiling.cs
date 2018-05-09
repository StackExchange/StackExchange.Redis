using System;
using System.Collections.Generic;
using System.Linq;
#if NETCOREAPP1_0
using System.Reflection;
#endif
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Profiling : TestBase
    {
        public Profiling(ITestOutputHelper output) : base (output) { }

        private class TestProfiler : IProfiler
        {
            public object MyContext = new object();
            public object GetContext() => MyContext;
        }

        [Fact]
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
                Assert.Equal("world", (string)val);
                var result=db.ScriptEvaluate(LuaScript.Prepare("return redis.call('get', @key)"), new { key = (RedisKey)"hello" });
                Assert.Equal("world", result.AsString());

                var cmds = conn.FinishProfiling(profiler.MyContext);
                Assert.Equal(3, cmds.Count());

                var set = cmds.SingleOrDefault(cmd => cmd.Command == "SET");
                Assert.NotNull(set);
                var get = cmds.SingleOrDefault(cmd => cmd.Command == "GET");
                Assert.NotNull(get);
                var eval = cmds.SingleOrDefault(cmd => cmd.Command == "EVAL");
                Assert.NotNull(eval);

                Assert.True(set.CommandCreated <= get.CommandCreated);
                Assert.True(get.CommandCreated <= eval.CommandCreated);

                AssertProfiledCommandValues(set, conn);

                AssertProfiledCommandValues(get, conn);

                AssertProfiledCommandValues(eval, conn);
            }
        }

        private static void AssertProfiledCommandValues(IProfiledCommand command, ConnectionMultiplexer conn)
        {
            Assert.Equal(4, command.Db);
            Assert.Equal(conn.GetEndPoints()[0], command.EndPoint);
            Assert.True(command.CreationToEnqueued > TimeSpan.Zero);
            Assert.True(command.EnqueuedToSending > TimeSpan.Zero);
            Assert.True(command.SentToResponse > TimeSpan.Zero);
            Assert.True(command.ResponseToCompletion > TimeSpan.Zero);
            Assert.True(command.ElapsedTime > TimeSpan.Zero);
            Assert.True(command.ElapsedTime > command.CreationToEnqueued && command.ElapsedTime > command.EnqueuedToSending && command.ElapsedTime > command.SentToResponse);
            Assert.True(command.RetransmissionOf == null);
            Assert.True(command.RetransmissionReason == null);
        }

        [Fact]
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

                    threads.Add(new Thread(() =>
                    {
                        var threadTasks = new List<Task>();

                        for (var j = 0; j < 1000; j++)
                        {
                            var task = db.StringSetAsync("" + j, "" + j);
                            threadTasks.Add(task);
                        }

                        Task.WaitAll(threadTasks.ToArray());
                    }));
                }

                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());

                var allVals = conn.FinishProfiling(profiler.MyContext);

                var kinds = allVals.Select(cmd => cmd.Command).Distinct().ToList();
                Assert.True(kinds.Count <= 2);
                Assert.Contains("SET", kinds);
                if (kinds.Count == 2 && !kinds.Contains("SELECT"))
                {
                    Assert.True(false, "Non-SET, Non-SELECT command seen");
                }

                Assert.Equal(16 * 1000, allVals.Count());
                Assert.Equal(16, allVals.Select(cmd => cmd.Db).Distinct().Count());

                for (var i = 0; i < 16; i++)
                {
                    var setsInDb = allVals.Count(cmd => cmd.Db == i && cmd.Command == "SET");
                    Assert.Equal(1000, setsInDb);
                }
            }
        }

        private class TestProfiler2 : IProfiler
        {
            private readonly ConcurrentDictionary<int, object> Contexts = new ConcurrentDictionary<int, object>();

            public void RegisterContext(object context)
            {
                Contexts[Thread.CurrentThread.ManagedThreadId] = context;
            }

            public object GetContext()
            {
                if (!Contexts.TryGetValue(Thread.CurrentThread.ManagedThreadId, out object ret)) ret = null;
                return ret;
            }
        }

        [Fact]
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
                    var thread = new Thread(() =>
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
                    });

                    threads.Add(thread);
                }

                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());

                for (var i = 0; i < results.Length; i++)
                {
                    var res = results[i];
                    Assert.NotNull(res);

                    var numGets = res.Count(r => r.Command == "GET");
                    var numSets = res.Count(r => r.Command == "SET");

                    Assert.Equal(1000, numGets);
                    Assert.Equal(1000, numSets);
                    Assert.True(res.All(cmd => cmd.Db == i));
                }
            }
        }

        private class TestProfiler3 : IProfiler
        {
            private readonly ConcurrentDictionary<int, object> Contexts = new ConcurrentDictionary<int, object>();

            public void RegisterContext(object context)
            {
                Contexts[Thread.CurrentThread.ManagedThreadId] = context;
            }

            public object AnyContext() => Contexts.First().Value;
            public void Reset() => Contexts.Clear();
            public object GetContext()
            {
                if (!Contexts.TryGetValue(Thread.CurrentThread.ManagedThreadId, out object ret)) ret = null;
                return ret;
            }
        }

        // This is a separate method for target=DEBUG purposes.
        // In release builds, the runtime is smart enough to figure out
        //   that the contexts are unreachable and should be collected but in
        //   debug builds... well, it's not very smart.
        private object LeaksCollectedAndRePooled_Initialize(ConnectionMultiplexer conn, int threadCount)
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
                var thread = new Thread(() =>
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
                });

                threads.Add(thread);
            }

            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            var anyContext = profiler.AnyContext();
            profiler.Reset();

            return anyContext;
        }

        [FactLongRunning]
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
                Assert.Equal(0, conn.profiledCommands.ContextCount);
                Assert.Equal(ThreadCount, ConcurrentProfileStorageCollection.AllocationCount);
                Assert.Equal(ThreadCount, ConcurrentProfileStorageCollection.CountInPool());
            }
        }

        [Fact]
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
                    var thread = new Thread(() =>
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
                    });

                    threads.Add(thread);
                }

                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());

                // only 16 allocations can ever be in flight at once
                var allocCount = ConcurrentProfileStorageCollection.AllocationCount;
                Assert.True(allocCount <= ThreadCount, allocCount.ToString());

                // correctness check for all allocations
                for (var i = 0; i < results.Length; i++)
                {
                    var resList = results[i];
                    foreach (var res in resList)
                    {
                        Assert.NotNull(res);

                        var numGets = res.Count(r => r.Command == "GET");
                        var numSets = res.Count(r => r.Command == "SET");

                        Assert.Equal(1000, numGets);
                        Assert.Equal(1000, numSets);
                        Assert.True(res.All(cmd => cmd.Db == i));
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
                            Assert.True(false, "Profilings were jumbled");
                        }
                    }
                }
            }
        }

        [Fact]
        public void LowAllocationEnumerable()
        {
            const int OuterLoop = 10000;

            using (var conn = Create())
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
                            async _ => (string)(await db.StringGetAsync("foo" + i).ForAwait())
                          );

                    var finalResult = t.Unwrap();
                    allTasks.Add(finalResult);
                }

                conn.WaitAll(allTasks.ToArray());

                var res = conn.FinishProfiling(profiler.MyContext);
                Assert.True(res.GetType().GetTypeInfo().IsValueType);

                using (var e = res.GetEnumerator())
                {
                    Assert.True(e.GetType().GetTypeInfo().IsValueType);

                    Assert.True(e.MoveNext());
                    var i = e.Current;

                    e.Reset();
                    Assert.True(e.MoveNext());
                    var j = e.Current;

                    Assert.True(object.ReferenceEquals(i, j));
                }

                Assert.Equal(OuterLoop * 2, res.Count());
                Assert.Equal(OuterLoop, res.Count(r => r.Command == "GET"));
                Assert.Equal(OuterLoop, res.Count(r => r.Command == "SET"));
            }
        }

        private class ToyProfiler : IProfiler
        {
            public ConcurrentDictionary<Thread, object> Contexts = new ConcurrentDictionary<Thread, object>();

            public object GetContext()
            {
                if (!Contexts.TryGetValue(Thread.CurrentThread, out object ctx)) ctx = null;
                return ctx;
            }
        }

        [Fact]
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

                    var thread = new Thread(() =>
                    {
                        var threadTasks = new List<Task>();

                        for (var j = 0; j < 1000; j++)
                        {
                            var task = db.StringSetAsync("" + j, "" + j);
                            threadTasks.Add(task);
                        }

                        Task.WaitAll(threadTasks.ToArray());
                    });

                    profiler.Contexts[thread] = thisGroupContext;

                    threads.Add(thread);
                }

                conn.BeginProfiling(thisGroupContext);

                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());

                IEnumerable<IProfiledCommand> timings = conn.FinishProfiling(thisGroupContext);

                Assert.Equal(16000, timings.Count());
            }
        }

        [Fact]
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

                    var thread = new Thread(() =>
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
                    });

                    profiler.Contexts[thread] = thread;

                    threads.Add(thread);
                }

                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());

                Assert.Equal(16, perThreadTimings.Count);
                Assert.True(perThreadTimings.All(kv => kv.Value.Count == 1000));
            }
        }
    }
}