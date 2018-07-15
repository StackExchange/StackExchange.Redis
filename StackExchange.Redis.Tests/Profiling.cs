﻿using System;
using System.Collections.Generic;
using System.Linq;
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
                var key = Me();

                conn.RegisterProfiler(profiler);
                conn.BeginProfiling(profiler.MyContext);
                var dbId = TestConfig.GetDedicatedDB();
                var db = conn.GetDatabase(dbId);
                db.StringSet(key, "world");
                var result = db.ScriptEvaluate(LuaScript.Prepare("return redis.call('get', @key)"), new { key = (RedisKey)key });
                Assert.Equal("world", result.AsString());
                var val = db.StringGet(key);
                Assert.Equal("world", (string)val);

                var cmds = conn.FinishProfiling(profiler.MyContext);
                var i = 0;
                foreach (var cmd in cmds)
                {
                    Log("Command {0} (DB: {1}): {2}", i++, cmd.Db, cmd.ToString().Replace("\n", ", "));
                }

                Log("Checking for SET");
                var set = cmds.SingleOrDefault(cmd => cmd.Command == "SET");
                Assert.NotNull(set);
                Log("Checking for GET");
                var get = cmds.SingleOrDefault(cmd => cmd.Command == "GET");
                Assert.NotNull(get);
                Log("Checking for EVAL");
                var eval = cmds.SingleOrDefault(cmd => cmd.Command == "EVAL");
                Assert.NotNull(eval);

                Assert.Equal(3, cmds.Count());

                Assert.True(set.CommandCreated <= eval.CommandCreated);
                Assert.True(eval.CommandCreated <= get.CommandCreated);

                AssertProfiledCommandValues(set, conn, dbId);

                AssertProfiledCommandValues(get, conn, dbId);

                AssertProfiledCommandValues(eval, conn, dbId);
            }
        }

        private static void AssertProfiledCommandValues(IProfiledCommand command, ConnectionMultiplexer conn, int dbId)
        {
            Assert.Equal(dbId, command.Db);
            Assert.Equal(conn.GetEndPoints()[0], command.EndPoint);
            Assert.True(command.CreationToEnqueued > TimeSpan.Zero, nameof(command.CreationToEnqueued));
            Assert.True(command.EnqueuedToSending > TimeSpan.Zero, nameof(command.EnqueuedToSending));
            Assert.True(command.SentToResponse > TimeSpan.Zero, nameof(command.SentToResponse));
            Assert.True(command.ResponseToCompletion > TimeSpan.Zero, nameof(command.ResponseToCompletion));
            Assert.True(command.ElapsedTime > TimeSpan.Zero, nameof(command.ElapsedTime));
            Assert.True(command.ElapsedTime > command.CreationToEnqueued && command.ElapsedTime > command.EnqueuedToSending && command.ElapsedTime > command.SentToResponse, "Comparisons");
            Assert.True(command.RetransmissionOf == null, nameof(command.RetransmissionOf));
            Assert.True(command.RetransmissionReason == null, nameof(command.RetransmissionReason));
        }

        [Fact]
        public void ManyThreads()
        {
            using (var conn = Create())
            {
                var profiler = new TestProfiler();
                var prefix = Me();

                conn.RegisterProfiler(profiler);
                conn.BeginProfiling(profiler.MyContext);

                var threads = new List<Thread>();
                const int CountPer = 100;
                for (var i = 1; i <= 16; i++)
                {
                    var db = conn.GetDatabase(i);

                    threads.Add(new Thread(() =>
                    {
                        var threadTasks = new List<Task>();

                        for (var j = 0; j < CountPer; j++)
                        {
                            var task = db.StringSetAsync(prefix + j, "" + j);
                            threadTasks.Add(task);
                        }

                        Task.WaitAll(threadTasks.ToArray());
                    }));
                }

                threads.ForEach(thread => thread.Start());
                threads.ForEach(thread => thread.Join());

                var allVals = conn.FinishProfiling(profiler.MyContext);
                var relevant = allVals.Where(cmd => cmd.Db > 0).ToList();

                var kinds = relevant.Select(cmd => cmd.Command).Distinct().ToList();
                foreach (var k in kinds)
                {
                    Log("Kind Seen: " + k);
                }
                Assert.True(kinds.Count <= 2);
                Assert.Contains("SET", kinds);
                if (kinds.Count == 2 && !kinds.Contains("SELECT") && !kinds.Contains("GET"))
                {
                    Assert.True(false, "Non-SET, Non-SELECT, Non-GET command seen");
                }

                Assert.Equal(16 * CountPer, relevant.Count);
                Assert.Equal(16, allVals.Select(cmd => cmd.Db).Distinct().Count());

                for (var i = 1; i <= 16; i++)
                {
                    var setsInDb = relevant.Count(cmd => cmd.Db == i);
                    Assert.Equal(CountPer, setsInDb);
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

        [FactLongRunning]
        public void ManyContexts()
        {
            using (var conn = Create())
            {
                var profiler = new TestProfiler2();
                var prefix = Me();
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
                            allTasks.Add(db.StringGetAsync(prefix + ix));
                            allTasks.Add(db.StringSetAsync(prefix + ix, "world" + ix));
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
        public async Task LeaksCollectedAndRePooled()
        {
            const int ThreadCount = 16;

            using (var conn = Create())
            {
                var anyContext = LeaksCollectedAndRePooled_Initialize(conn, ThreadCount);

                // force collection of everything but `anyContext`
                GC.Collect(3, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();

                await Task.Delay(TimeSpan.FromMinutes(1.01)).ForAwait();
                conn.FinishProfiling(anyContext);

                // make sure we haven't left anything in the active contexts dictionary
                Assert.Equal(0, conn.profiledCommands.ContextCount);
                Assert.Equal(ThreadCount, ConcurrentProfileStorageCollection.AllocationCount);
                Assert.Equal(ThreadCount, ConcurrentProfileStorageCollection.CountInPool());
            }
        }

        [FactLongRunning]
        public void ReuseStorage()
        {
            const int ThreadCount = 16;

            // have to reset so other tests don't clober
            ConcurrentProfileStorageCollection.AllocationCount = 0;

            using (var conn = Create())
            {
                var profiler = new TestProfiler2();
                var prefix = Me();
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
                                allTasks.Add(db.StringGetAsync(prefix + ix));
                                allTasks.Add(db.StringSetAsync(prefix + ix, "world" + ix));
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
            const int OuterLoop = 1000;

            using (var conn = Create())
            {
                var profiler = new TestProfiler();
                conn.RegisterProfiler(profiler);

                conn.BeginProfiling(profiler.MyContext);

                var prefix = Me();
                var db = conn.GetDatabase(1);

                var allTasks = new List<Task<string>>();

                foreach (var i in Enumerable.Range(0, OuterLoop))
                {
                    var t =
                        db.StringSetAsync(prefix + i, "bar" + i)
                          .ContinueWith(
                            async _ => (string)(await db.StringGetAsync(prefix + i).ForAwait())
                          );

                    var finalResult = t.Unwrap();
                    allTasks.Add(finalResult);
                }

                conn.WaitAll(allTasks.ToArray());

                var res = conn.FinishProfiling(profiler.MyContext);
                Assert.True(res.GetType().IsValueType);

                using (var e = res.GetEnumerator())
                {
                    Assert.True(e.GetType().IsValueType);

                    Assert.True(e.MoveNext());
                    var i = e.Current;

                    e.Reset();
                    Assert.True(e.MoveNext());
                    var j = e.Current;

                    Assert.True(object.ReferenceEquals(i, j));
                }

                Assert.Equal(OuterLoop, res.Count(r => r.Command == "GET" && r.Db > 0));
                Assert.Equal(OuterLoop, res.Count(r => r.Command == "SET" && r.Db > 0));
                Assert.Equal(OuterLoop * 2, res.Count(r => r.Db > 0));
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

        [FactLongRunning]
        public void ProfilingMD_Ex1()
        {
            using (var c = Create())
            {
                ConnectionMultiplexer conn = c;
                var profiler = new ToyProfiler();
                var prefix = Me();
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
                            var task = db.StringSetAsync(prefix + j, "" + j);
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

        [FactLongRunning]
        public void ProfilingMD_Ex2()
        {
            using (var c = Create())
            {
                ConnectionMultiplexer conn = c;
                var profiler = new ToyProfiler();
                var prefix = Me();

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
                            var task = db.StringSetAsync(prefix + j, "" + j);
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
