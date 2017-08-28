using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#if FEATURE_BOOKSLEEVE
using BookSleeve;
#endif
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class BasicOpsTests : TestBase
    {
        public BasicOpsTests(ITestOutputHelper output) : base (output) { }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PingOnce(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var conn = muxer.GetDatabase();

                var task = conn.PingAsync();
                var duration = muxer.Wait(task);
                Output.WriteLine("Ping took: " + duration);
                Assert.True(duration.TotalMilliseconds > 0);
            }
        }

        [Fact]
        public void RapidDispose()
        {
            RedisKey key = Me();
            using (var primary = Create())
            {
                var conn = primary.GetDatabase();
                conn.KeyDelete(key);

                for (int i = 0; i < 10; i++)
                {
                    using (var secondary = Create(fail: true))
                    {
                        secondary.GetDatabase().StringIncrement(key, flags: CommandFlags.FireAndForget);
                    }
                }
                Assert.Equal(10, (int)conn.StringGet(key));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PingMany(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var conn = muxer.GetDatabase();
                var tasks = new Task<TimeSpan>[10000];

                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = conn.PingAsync();
                }
                muxer.WaitAll(tasks);
                Assert.True(tasks[0].Result.TotalMilliseconds > 0);
                Assert.True(tasks[tasks.Length - 1].Result.TotalMilliseconds > 0);
            }
        }

        [Fact]
        public void GetWithNullKey()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                const string key = null;
                var ex = Assert.Throws<ArgumentException>(() => db.StringGet(key));
                Assert.Equal("A null key is not valid in this context", ex.Message);
            }
        }

        [Fact]
        public void SetWithNullKey()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                const string key = null, value = "abc";
                var ex = Assert.Throws<ArgumentException>(() => db.StringSet(key, value));
                Assert.Equal("A null key is not valid in this context", ex.Message);
            }
        }

        [Fact]
        public void SetWithNullValue()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                string key = Me(), value = null;
                db.KeyDelete(key, CommandFlags.FireAndForget);

                db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                Assert.True(db.KeyExists(key));
                db.StringSet(key, value);

                var actual = (string)db.StringGet(key);
                Assert.Null(actual);
                Assert.False(db.KeyExists(key));
            }
        }

        [Fact]
        public void SetWithDefaultValue()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                string key = Me();
                var value = default(RedisValue); // this is kinda 0... ish
                db.KeyDelete(key, CommandFlags.FireAndForget);

                db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                Assert.True(db.KeyExists(key));
                db.StringSet(key, value);

                var actual = (string)db.StringGet(key);
                Assert.Null(actual);
                Assert.False(db.KeyExists(key));
            }
        }

        [Fact]
        public void SetWithZeroValue()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                string key = Me();
                const long value = 0;
                db.KeyDelete(key, CommandFlags.FireAndForget);

                db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                Assert.True(db.KeyExists(key));
                db.StringSet(key, value);

                var actual = (string)db.StringGet(key);
                Assert.Equal("0", actual);
                Assert.True(db.KeyExists(key));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GetSetAsync(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var conn = muxer.GetDatabase();

                RedisKey key = Me();
                var d0 = conn.KeyDeleteAsync(key);
                var d1 = conn.KeyDeleteAsync(key);
                var g1 = conn.StringGetAsync(key);
                var s1 = conn.StringSetAsync(key, "123");
                var g2 = conn.StringGetAsync(key);
                var d2 = conn.KeyDeleteAsync(key);

                await d0;
                Assert.False(await d1);
                Assert.Null((string)(await g1));
                Assert.True((await g1).IsNull);
                await s1;
                Assert.Equal("123", await g2);
                Assert.Equal(123, (int)(await g2));
                Assert.False((await g2).IsNull);
                Assert.True(await d2);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetSetSync(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var conn = muxer.GetDatabase();

                RedisKey key = Me();
                conn.KeyDelete(key);
                var d1 = conn.KeyDelete(key);
                var g1 = conn.StringGet(key);
                conn.StringSet(key, "123");
                var g2 = conn.StringGet(key);
                var d2 = conn.KeyDelete(key);

                Assert.False(d1);
                Assert.Null((string)g1);
                Assert.True(g1.IsNull);

                Assert.Equal("123", (string)g2);
                Assert.Equal(123, (int)g2);
                Assert.False(g2.IsNull);
                Assert.True(d2);
            }
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void MassiveBulkOpsAsync(bool preserveOrder, bool withContinuation)
        {
#if DEBUG
            var oldAsyncCompletionCount = ConnectionMultiplexer.GetAsyncCompletionWorkerCount();
#endif
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                RedisKey key = "MBOA";
                var conn = muxer.GetDatabase();
                muxer.Wait(conn.PingAsync());

#if CORE_CLR
                int number = 0;
#endif
                Action<Task> nonTrivial = delegate
                {
#if !CORE_CLR
                    Thread.SpinWait(5);
#else
                    for (int i = 0; i < 50; i++)
                    {
                        number++;
                    }
#endif
                };
                var watch = Stopwatch.StartNew();
                for (int i = 0; i <= AsyncOpsQty; i++)
                {
                    var t = conn.StringSetAsync(key, i);
                    if (withContinuation) t.ContinueWith(nonTrivial);
                }
                int val = (int)muxer.Wait(conn.StringGetAsync(key));
                Assert.Equal(AsyncOpsQty, val);
                watch.Stop();
                Output.WriteLine("{2}: Time for {0} ops: {1}ms ({3}, {4}); ops/s: {5}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    withContinuation ? "with continuation" : "no continuation", preserveOrder ? "preserve order" : "any order",
                    AsyncOpsQty / watch.Elapsed.TotalSeconds);
#if DEBUG
                Output.WriteLine("Async completion workers: " + (ConnectionMultiplexer.GetAsyncCompletionWorkerCount() - oldAsyncCompletionCount));
#endif
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public void GetWithExpiry(bool exists, bool hasExpiry)
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key);
                if (exists)
                {
                    if (hasExpiry)
                        db.StringSet(key, "val", TimeSpan.FromMinutes(5));
                    else
                        db.StringSet(key, "val");
                }
                var async = db.StringGetWithExpiryAsync(key);
                var syncResult = db.StringGetWithExpiry(key);
                var asyncResult = db.Wait(async);

                if (exists)
                {
                    Assert.Equal("val", (string)asyncResult.Value);
                    Assert.Equal(hasExpiry, asyncResult.Expiry.HasValue);
                    if (hasExpiry) Assert.True(asyncResult.Expiry.Value.TotalMinutes >= 4.9 && asyncResult.Expiry.Value.TotalMinutes <= 5);
                    Assert.Equal("val", (string)syncResult.Value);
                    Assert.Equal(hasExpiry, syncResult.Expiry.HasValue);
                    if (hasExpiry) Assert.True(syncResult.Expiry.Value.TotalMinutes >= 4.9 && syncResult.Expiry.Value.TotalMinutes <= 5);
                }
                else
                {
                    Assert.True(asyncResult.Value.IsNull);
                    Assert.False(asyncResult.Expiry.HasValue);
                    Assert.True(syncResult.Value.IsNull);
                    Assert.False(syncResult.Expiry.HasValue);
                }
            }
        }

        [Fact]
        public void GetWithExpiryWrongTypeAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key);
                db.SetAdd(key, "abc");
                var ex = Assert.Throws<RedisServerException>(() =>
                {
                    try
                    {
                        Output.WriteLine("Key: " + (string)key);
                        var async = db.Wait(db.StringGetWithExpiryAsync(key));
                    }
                    catch (AggregateException e)
                    {
                        throw e.InnerExceptions[0];
                    }
                });
                Assert.Equal("WRONGTYPE Operation against a key holding the wrong kind of value", ex.Message);
            }
        }

        [Fact]
        public void GetWithExpiryWrongTypeSync()
        {
            var ex = Assert.Throws<RedisServerException>(() =>
            {
                using (var conn = Create())
                {
                    var db = conn.GetDatabase();
                    RedisKey key = Me();
                    db.KeyDelete(key);
                    db.SetAdd(key, "abc");
                    db.StringGetWithExpiry(key);
                }
            });
            Assert.Equal("WRONGTYPE Operation against a key holding the wrong kind of value", ex.Message);
        }

#if FEATURE_BOOKSLEEVE
        [Theory]
        [InlineData(true, true, ResultCompletionMode.ConcurrentIfContinuation)]
        [InlineData(true, false, ResultCompletionMode.ConcurrentIfContinuation)]
        [InlineData(false, true, ResultCompletionMode.ConcurrentIfContinuation)]
        [InlineData(false, false, ResultCompletionMode.ConcurrentIfContinuation)]
        [InlineData(true, true, ResultCompletionMode.Concurrent)]
        [InlineData(true, false, ResultCompletionMode.Concurrent)]
        [InlineData(false, true, ResultCompletionMode.Concurrent)]
        [InlineData(false, false, ResultCompletionMode.Concurrent)]
        [InlineData(true, true, ResultCompletionMode.PreserveOrder)]
        [InlineData(true, false, ResultCompletionMode.PreserveOrder)]
        [InlineData(false, true, ResultCompletionMode.PreserveOrder)]
        [InlineData(false, false, ResultCompletionMode.PreserveOrder)]
        public void MassiveBulkOpsAsyncOldStyle(bool withContinuation, bool suspendFlush, ResultCompletionMode completionMode)
        {
            using (var conn = GetOldStyleConnection())
            {
                const int db = 0;
                string key = "MBOQ";
                conn.CompletionMode = completionMode;
                conn.Wait(conn.Server.Ping());
                Action<Task> nonTrivial = delegate
                {
                    Thread.SpinWait(5);
                };
                var watch = Stopwatch.StartNew();

                if (suspendFlush) conn.SuspendFlush();
                try
                {

                    for (int i = 0; i <= AsyncOpsQty; i++)
                    {
                        var t = conn.Strings.Set(db, key, i);
                        if (withContinuation) t.ContinueWith(nonTrivial);
                    }
                } finally
                {
                    if (suspendFlush) conn.ResumeFlush();
                }
                int val = (int)conn.Wait(conn.Strings.GetInt64(db, key));
                Assert.Equal(AsyncOpsQty, val);
                watch.Stop();
                Output.WriteLine("{2}: Time for {0} ops: {1}ms ({3}, {4}, {5}); ops/s: {6}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    withContinuation ? "with continuation" : "no continuation",
                    suspendFlush ? "suspend flush" : "flush at whim",
                    completionMode, AsyncOpsQty / watch.Elapsed.TotalSeconds);
            }
        }
#endif

        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 1)]
        [InlineData(true, 5)]
        [InlineData(false, 5)]
        [InlineData(true, 10)]
        [InlineData(false, 10)]
        [InlineData(true, 50)]
        [InlineData(false, 50)]
        public void MassiveBulkOpsSync(bool preserveOrder, int threads)
        {
            int workPerThread = SyncOpsQty / threads;
            using (var muxer = Create(syncTimeout: 20000))
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                RedisKey key = "MBOS";
                var conn = muxer.GetDatabase();
                conn.KeyDelete(key);
#if DEBUG
                long oldAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
                long oldWorkerCount = ConnectionMultiplexer.GetAsyncCompletionWorkerCount();
#endif
                var timeTaken = RunConcurrent(delegate
                {
                    for (int i = 0; i < workPerThread; i++)
                    {
                        conn.StringIncrement(key);
                    }
                }, threads);

                int val = (int)conn.StringGet(key);
                Assert.Equal(workPerThread * threads, val);
                Output.WriteLine("{2}: Time for {0} ops on {4} threads: {1}ms ({3}); ops/s: {5}",
                    threads * workPerThread, timeTaken.TotalMilliseconds, Me()
                    , preserveOrder ? "preserve order" : "any order", threads, (workPerThread * threads) / timeTaken.TotalSeconds);
#if DEBUG
                long newAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
                long newWorkerCount = ConnectionMultiplexer.GetAsyncCompletionWorkerCount();
                Output.WriteLine("ResultBox allocations: {0}; workers {1}", newAlloc - oldAlloc, newWorkerCount - oldWorkerCount);
                Assert.True(newAlloc - oldAlloc <= 2 * threads, "number of box allocations");
#endif
            }
        }

#if FEATURE_BOOKSLEEVE
        [Theory]
        [InlineData(ResultCompletionMode.Concurrent, 1)]
        [InlineData(ResultCompletionMode.ConcurrentIfContinuation, 1)]
        [InlineData(ResultCompletionMode.PreserveOrder, 1)]
        [InlineData(ResultCompletionMode.Concurrent, 5)]
        [InlineData(ResultCompletionMode.ConcurrentIfContinuation, 5)]
        [InlineData(ResultCompletionMode.PreserveOrder, 5)]
        [InlineData(ResultCompletionMode.Concurrent, 10)]
        [InlineData(ResultCompletionMode.ConcurrentIfContinuation, 10)]
        [InlineData(ResultCompletionMode.PreserveOrder, 10)]
        [InlineData(ResultCompletionMode.Concurrent, 50)]
        [InlineData(ResultCompletionMode.ConcurrentIfContinuation, 50)]
        [InlineData(ResultCompletionMode.PreserveOrder, 50)]
        public void MassiveBulkOpsSyncOldStyle(ResultCompletionMode completionMode, int threads)
        {
            int workPerThread = SyncOpsQty / threads;

            using (var conn = GetOldStyleConnection())
            {
                const int db = 0;
                string key = "MBOQ";
                conn.CompletionMode = completionMode;
                conn.Wait(conn.Keys.Remove(db, key));

                var timeTaken = RunConcurrent(delegate
                {
                    for (int i = 0; i < workPerThread; i++)
                    {
                        conn.Wait(conn.Strings.Increment(db, key));
                    }
                }, threads);

                int val = (int)conn.Wait(conn.Strings.GetInt64(db, key));
                Assert.Equal(workPerThread * threads, val);

                Output.WriteLine("{2}: Time for {0} ops on {4} threads: {1}ms ({3}); ops/s: {5}", workPerThread * threads, timeTaken.TotalMilliseconds, Me(),
                    completionMode, threads, (workPerThread * threads) / timeTaken.TotalSeconds);
            }
        }
#endif

        [Theory]
        [InlineData(true, 1)]
        [InlineData(false, 1)]
        [InlineData(true, 5)]
        [InlineData(false, 5)]
        public void MassiveBulkOpsFireAndForget(bool preserveOrder, int threads)
        {
            using (var muxer = Create(syncTimeout: 20000))
            {
                muxer.PreserveAsyncOrder = preserveOrder;
#if DEBUG
                long oldAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
#endif
                RedisKey key = "MBOF";
                var conn = muxer.GetDatabase();
                conn.Ping();

                conn.KeyDelete(key, CommandFlags.FireAndForget);
                int perThread = AsyncOpsQty / threads;
                var elapsed = RunConcurrent(delegate
                {
                    for (int i = 0; i < perThread; i++)
                    {
                        conn.StringIncrement(key, flags: CommandFlags.FireAndForget);
                    }
                    conn.Ping();
                }, threads);
                var val = (long)conn.StringGet(key);
                Assert.Equal(perThread * threads, val);

                Output.WriteLine("{2}: Time for {0} ops over {5} threads: {1:###,###}ms ({3}); ops/s: {4:###,###,##0}",
                    val, elapsed.TotalMilliseconds, Me(),
                    preserveOrder ? "preserve order" : "any order",
                    val / elapsed.TotalSeconds, threads);
#if DEBUG
                long newAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
                Output.WriteLine("ResultBox allocations: {0}",
                    newAlloc - oldAlloc);
                Assert.True(newAlloc - oldAlloc <= 4);
#endif
            }
        }

#if DEBUG
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestQuit(bool preserveOrder)
        {
            SetExpectedAmbientFailureCount(1);
            using (var muxer = Create(allowAdmin: true))
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var db = muxer.GetDatabase();
                string key = Guid.NewGuid().ToString();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, key, flags: CommandFlags.FireAndForget);
                GetServer(muxer).Quit(CommandFlags.FireAndForget);
                var watch = Stopwatch.StartNew();
                Assert.Throws<RedisConnectionException>(() => db.Ping());
                watch.Stop();
                Output.WriteLine("Time to notice quit: {0}ms ({1})", watch.ElapsedMilliseconds,
                    preserveOrder ? "preserve order" : "any order");
                Thread.Sleep(20);
                Debug.WriteLine("Pinging...");
                Assert.Equal(key, (string)db.StringGet(key));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestSevered(bool preserveOrder)
        {
            SetExpectedAmbientFailureCount(2);
            using (var muxer = Create(allowAdmin: true))
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var db = muxer.GetDatabase();
                string key = Guid.NewGuid().ToString();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, key, flags: CommandFlags.FireAndForget);
                GetServer(muxer).SimulateConnectionFailure();
                var watch = Stopwatch.StartNew();
                db.Ping();
                watch.Stop();
                Output.WriteLine("Time to re-establish: {0}ms ({1})", watch.ElapsedMilliseconds,
                    preserveOrder ? "preserve order" : "any order");
                await Task.Delay(200);
                Debug.WriteLine("Pinging...");
                Assert.Equal(key, db.StringGet(key));
            }
        }
#endif

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IncrAsync(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var conn = muxer.GetDatabase();
                RedisKey key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);
                var nix = conn.KeyExistsAsync(key);
                var a = conn.StringGetAsync(key);
                var b = conn.StringIncrementAsync(key);
                var c = conn.StringGetAsync(key);
                var d = conn.StringIncrementAsync(key, 10);
                var e = conn.StringGetAsync(key);
                var f = conn.StringDecrementAsync(key, 11);
                var g = conn.StringGetAsync(key);
                var h = conn.KeyExistsAsync(key);
                Assert.False(muxer.Wait(nix));
                Assert.True(muxer.Wait(a).IsNull);
                Assert.Equal(0, (long)muxer.Wait(a));
                Assert.Equal(1, muxer.Wait(b));
                Assert.Equal(1, (long)muxer.Wait(c));
                Assert.Equal(11, muxer.Wait(d));
                Assert.Equal(11, (long)muxer.Wait(e));
                Assert.Equal(0, muxer.Wait(f));
                Assert.Equal(0, (long)muxer.Wait(g));
                Assert.True(muxer.Wait(h));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IncrSync(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var conn = muxer.GetDatabase();
                RedisKey key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);
                var nix = conn.KeyExists(key);
                var a = conn.StringGet(key);
                var b = conn.StringIncrement(key);
                var c = conn.StringGet(key);
                var d = conn.StringIncrement(key, 10);
                var e = conn.StringGet(key);
                var f = conn.StringDecrement(key, 11);
                var g = conn.StringGet(key);
                var h = conn.KeyExists(key);
                Assert.False(nix);
                Assert.True(a.IsNull);
                Assert.Equal(0, (long)a);
                Assert.Equal(1, b);
                Assert.Equal(1, (long)c);
                Assert.Equal(11, d);
                Assert.Equal(11, (long)e);
                Assert.Equal(0, f);
                Assert.Equal(0, (long)g);
                Assert.True(h);
            }
        }

        [Fact]
        public void IncrDifferentSizes()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                int expected = 0;
                Incr(db, key, -129019, ref expected);
                Incr(db, key, -10023, ref expected);
                Incr(db, key, -9933, ref expected);
                Incr(db, key, -23, ref expected);
                Incr(db, key, -7, ref expected);
                Incr(db, key, -1, ref expected);
                Incr(db, key, 0, ref expected);
                Incr(db, key, 1, ref expected);
                Incr(db, key, 9, ref expected);
                Incr(db, key, 11, ref expected);
                Incr(db, key, 345, ref expected);
                Incr(db, key, 4982, ref expected);
                Incr(db, key, 13091, ref expected);
                Incr(db, key, 324092, ref expected);
                Assert.NotEqual(0, expected);
                var sum = (long)db.StringGet(key);
                Assert.Equal(expected, sum);
            }
        }

        private void Incr(IDatabase database, RedisKey key, int delta, ref int total)
        {
            database.StringIncrement(key, delta, CommandFlags.FireAndForget);
            total += delta;
        }

        [Fact]
        public void WrappedDatabasePrefixIntegration()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase().WithKeyPrefix("abc");
                db.KeyDelete("count");
                db.StringIncrement("count");
                db.StringIncrement("count");
                db.StringIncrement("count");

                int count = (int)conn.GetDatabase().StringGet("abccount");
                Assert.Equal(3, count);
            }
        }
    }
}
