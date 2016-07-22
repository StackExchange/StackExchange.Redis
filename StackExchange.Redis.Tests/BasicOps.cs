using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#if FEATURE_BOOKSLEEVE
using BookSleeve;
#endif
using NUnit.Framework;
using StackExchange.Redis.KeyspaceIsolation;
namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class BasicOpsTests : TestBase
    {
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void PingOnce(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var conn = muxer.GetDatabase();

                var task = conn.PingAsync();
                var duration = muxer.Wait(task);
                Console.WriteLine("Ping took: " + duration);
                Assert.IsTrue(duration.TotalMilliseconds > 0);
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void RapidDispose(bool preserverOrder)
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

                Assert.AreEqual(10, (int)conn.StringGet(key));
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
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
                Assert.IsTrue(tasks[0].Result.TotalMilliseconds > 0);
                Assert.IsTrue(tasks[tasks.Length - 1].Result.TotalMilliseconds > 0);
            }
        }

        [Test]
        public void GetWithNullKey()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                string key = null;
                Assert.Throws<ArgumentException>(
                    () => db.StringGet(key),
                    "A null key is not valid in this context");
            }
        }

        [Test]
        public void SetWithNullKey()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                string key = null, value = "abc";
                Assert.Throws<ArgumentException>(
                    () => db.StringSet(key, value),
                    "A null key is not valid in this context");
            }
        }

        [Test]
        public void SetWithNullValue()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                string key = Me(), value = null;
                db.KeyDelete(key, CommandFlags.FireAndForget);

                db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                Assert.IsTrue(db.KeyExists(key));
                db.StringSet(key, value);
                
                var actual = (string)db.StringGet(key);
                Assert.IsNull(actual);
                Assert.IsFalse(db.KeyExists(key));
            }
        }

        [Test]
        public void SetWithDefaultValue()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                string key = Me();
                var value = default(RedisValue); // this is kinda 0... ish
                db.KeyDelete(key, CommandFlags.FireAndForget);

                db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                Assert.IsTrue(db.KeyExists(key));
                db.StringSet(key, value);

                var actual = (string)db.StringGet(key);
                Assert.IsNull(actual);
                Assert.IsFalse(db.KeyExists(key));
            }
        }

        [Test]
        public void SetWithZeroValue()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                string key = Me();
                long value = 0;
                db.KeyDelete(key, CommandFlags.FireAndForget);

                db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                Assert.IsTrue(db.KeyExists(key));
                db.StringSet(key, value);

                var actual = (string)db.StringGet(key);
                Assert.AreEqual("0", actual);
                Assert.IsTrue(db.KeyExists(key));
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void GetSetAsync(bool preserveOrder)
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

                muxer.Wait(d0);
                Assert.IsFalse(muxer.Wait(d1));
                Assert.IsNull((string)muxer.Wait(g1));
                Assert.IsTrue(muxer.Wait(g1).IsNull);
                muxer.Wait(s1);
                Assert.AreEqual("123", (string)muxer.Wait(g2));
                Assert.AreEqual(123, (int)muxer.Wait(g2));
                Assert.IsFalse(muxer.Wait(g2).IsNull);
                Assert.IsTrue(muxer.Wait(d2));
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
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

                Assert.IsFalse(d1);
                Assert.IsNull((string)g1);
                Assert.IsTrue(g1.IsNull);

                Assert.AreEqual("123", (string)g2);
                Assert.AreEqual(123, (int)g2);
                Assert.IsFalse(g2.IsNull);
                Assert.IsTrue(d2);
            }
        }

        [Test]
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
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
                Assert.AreEqual(AsyncOpsQty, val);
                watch.Stop();
                Console.WriteLine("{2}: Time for {0} ops: {1}ms ({3}, {4}); ops/s: {5}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    withContinuation ? "with continuation" : "no continuation", preserveOrder ? "preserve order" : "any order",
                    AsyncOpsQty / watch.Elapsed.TotalSeconds);
#if DEBUG
                Console.WriteLine("Async completion workers: " + (ConnectionMultiplexer.GetAsyncCompletionWorkerCount() - oldAsyncCompletionCount));
#endif
            }
        }

        [Test]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void GetWithExpiry(bool exists, bool hasExpiry)
        {
            using(var conn = Create())
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

                if(exists)
                {
                    Assert.AreEqual("val", (string)asyncResult.Value);
                    Assert.AreEqual(hasExpiry, asyncResult.Expiry.HasValue);
                    if (hasExpiry) Assert.IsTrue(asyncResult.Expiry.Value.TotalMinutes >= 4.9 && asyncResult.Expiry.Value.TotalMinutes <= 5);
                    Assert.AreEqual("val", (string)syncResult.Value);
                    Assert.AreEqual(hasExpiry, syncResult.Expiry.HasValue);
                    if (hasExpiry) Assert.IsTrue(syncResult.Expiry.Value.TotalMinutes >= 4.9 && syncResult.Expiry.Value.TotalMinutes <= 5);
                }
                else
                {
                    Assert.IsTrue(asyncResult.Value.IsNull);
                    Assert.IsFalse(asyncResult.Expiry.HasValue);
                    Assert.IsTrue(syncResult.Value.IsNull);
                    Assert.IsFalse(syncResult.Expiry.HasValue);
                }
            }
        }
        [Test]
        public void GetWithExpiryWrongTypeAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key);
                db.SetAdd(key, "abc");
                Assert.Throws<RedisServerException>(() =>
                {
                    try
                    {
                        var async = db.Wait(db.StringGetWithExpiryAsync(key));
                    }
                    catch (AggregateException ex)
                    {
                        throw ex.InnerExceptions[0];
                    }
                    Assert.Fail();
                },
                "A null key is not valid in this context");
            }
        }

        [Test]
        public void GetWithExpiryWrongTypeSync()
        {
            Assert.Throws<RedisServerException>(() =>
            {
                using (var conn = Create())
                {
                    var db = conn.GetDatabase();
                    RedisKey key = Me();
                    db.KeyDelete(key);
                    db.SetAdd(key, "abc");
                    db.StringGetWithExpiry(key);
                    Assert.Fail();
                }
            },
            "WRONGTYPE Operation against a key holding the wrong kind of value");
        }

#if FEATURE_BOOKSLEEVE
        [Test]
        [TestCase(true, true, ResultCompletionMode.ConcurrentIfContinuation)]
        [TestCase(true, false, ResultCompletionMode.ConcurrentIfContinuation)]
        [TestCase(false, true, ResultCompletionMode.ConcurrentIfContinuation)]
        [TestCase(false, false, ResultCompletionMode.ConcurrentIfContinuation)]
        [TestCase(true, true, ResultCompletionMode.Concurrent)]
        [TestCase(true, false, ResultCompletionMode.Concurrent)]
        [TestCase(false, true, ResultCompletionMode.Concurrent)]
        [TestCase(false, false, ResultCompletionMode.Concurrent)]
        [TestCase(true, true, ResultCompletionMode.PreserveOrder)]
        [TestCase(true, false, ResultCompletionMode.PreserveOrder)]
        [TestCase(false, true, ResultCompletionMode.PreserveOrder)]
        [TestCase(false, false, ResultCompletionMode.PreserveOrder)]
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
                Assert.AreEqual(AsyncOpsQty, val);
                watch.Stop();
                Console.WriteLine("{2}: Time for {0} ops: {1}ms ({3}, {4}, {5}); ops/s: {6}", AsyncOpsQty, watch.ElapsedMilliseconds, Me(),
                    withContinuation ? "with continuation" : "no continuation",
                    suspendFlush ? "suspend flush" : "flush at whim",
                    completionMode, AsyncOpsQty / watch.Elapsed.TotalSeconds);
            }
        }
#endif

        [Test]
        [TestCase(true, 1)]
        [TestCase(false, 1)]
        [TestCase(true, 5)]
        [TestCase(false, 5)]
        [TestCase(true, 10)]
        [TestCase(false, 10)]
        [TestCase(true, 50)]
        [TestCase(false, 50)]
        public void MassiveBulkOpsSync(bool preserveOrder, int threads)
        {
            int workPerThread = SyncOpsQty / threads;
            using (var muxer = Create())
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
                Assert.AreEqual(workPerThread * threads, val);
                Console.WriteLine("{2}: Time for {0} ops on {4} threads: {1}ms ({3}); ops/s: {5}",
                    threads * workPerThread, timeTaken.TotalMilliseconds, Me()
                    , preserveOrder ? "preserve order" : "any order", threads, (workPerThread * threads) / timeTaken.TotalSeconds);
#if DEBUG
                long newAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
                long newWorkerCount = ConnectionMultiplexer.GetAsyncCompletionWorkerCount();
                Console.WriteLine("ResultBox allocations: {0}; workers {1}", newAlloc - oldAlloc, newWorkerCount - oldWorkerCount);
                Assert.IsTrue(newAlloc - oldAlloc <= 2 * threads, "number of box allocations");
#endif
            }
        }

#if FEATURE_BOOKSLEEVE
        [Test]
        [TestCase(ResultCompletionMode.Concurrent, 1)]
        [TestCase(ResultCompletionMode.ConcurrentIfContinuation, 1)]
        [TestCase(ResultCompletionMode.PreserveOrder, 1)]
        [TestCase(ResultCompletionMode.Concurrent, 5)]
        [TestCase(ResultCompletionMode.ConcurrentIfContinuation, 5)]
        [TestCase(ResultCompletionMode.PreserveOrder, 5)]
        [TestCase(ResultCompletionMode.Concurrent, 10)]
        [TestCase(ResultCompletionMode.ConcurrentIfContinuation, 10)]
        [TestCase(ResultCompletionMode.PreserveOrder, 10)]
        [TestCase(ResultCompletionMode.Concurrent, 50)]
        [TestCase(ResultCompletionMode.ConcurrentIfContinuation, 50)]
        [TestCase(ResultCompletionMode.PreserveOrder, 50)]
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
                Assert.AreEqual(workPerThread * threads, val);

                Console.WriteLine("{2}: Time for {0} ops on {4} threads: {1}ms ({3}); ops/s: {5}", workPerThread * threads, timeTaken.TotalMilliseconds, Me(),
                    completionMode, threads, (workPerThread * threads) / timeTaken.TotalSeconds);
            }
        }
#endif

        [Test]
        [TestCase(true, 1)]
        [TestCase(false, 1)]
        [TestCase(true, 5)]
        [TestCase(false, 5)]
        public void MassiveBulkOpsFireAndForget(bool preserveOrder, int threads)
        {
            using (var muxer = Create())
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
                Assert.AreEqual(perThread * threads, val);
                
                Console.WriteLine("{2}: Time for {0} ops over {5} threads: {1:###,###}ms ({3}); ops/s: {4:###,###,##0}",
                    val, elapsed.TotalMilliseconds, Me(),
                    preserveOrder ? "preserve order" : "any order",
                    val / elapsed.TotalSeconds, threads);
#if DEBUG
                long newAlloc = ConnectionMultiplexer.GetResultBoxAllocationCount();
                Console.WriteLine("ResultBox allocations: {0}",
                    newAlloc - oldAlloc);
                Assert.IsTrue(newAlloc - oldAlloc <= 4);
#endif
            }
        }


#if DEBUG
        [Test]
        [TestCase(true)]
        [TestCase(false)]
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
                try
                {
                    db.Ping();
                    Assert.Fail();
                }
                catch (RedisConnectionException) { }
                watch.Stop();
                Console.WriteLine("Time to notice quit: {0}ms ({1})", watch.ElapsedMilliseconds,
                    preserveOrder ? "preserve order" : "any order");
                Thread.Sleep(20);
                Debug.WriteLine("Pinging...");
                Assert.AreEqual(key, (string)db.StringGet(key));
            }
        }
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void TestSevered(bool preserveOrder)
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
                Console.WriteLine("Time to re-establish: {0}ms ({1})", watch.ElapsedMilliseconds,
                    preserveOrder ? "preserve order" : "any order");
                Thread.Sleep(20);
                Debug.WriteLine("Pinging...");
                Assert.AreEqual(key, (string)db.StringGet(key));
            }
        }
#endif



        [Test]
        [TestCase(true)]
        [TestCase(false)]
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
                Assert.IsFalse(muxer.Wait(nix));
                Assert.IsTrue(muxer.Wait(a).IsNull);
                Assert.AreEqual(0, (long)muxer.Wait(a));
                Assert.AreEqual(1, muxer.Wait(b));
                Assert.AreEqual(1, (long)muxer.Wait(c));
                Assert.AreEqual(11, muxer.Wait(d));
                Assert.AreEqual(11, (long)muxer.Wait(e));
                Assert.AreEqual(0, muxer.Wait(f));
                Assert.AreEqual(0, (long)muxer.Wait(g));
                Assert.IsTrue(muxer.Wait(h));
            }
        }
        [Test]
        [TestCase(true)]
        [TestCase(false)]
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
                Assert.IsFalse(nix);
                Assert.IsTrue(a.IsNull);
                Assert.AreEqual(0, (long)a);
                Assert.AreEqual(1, b);
                Assert.AreEqual(1, (long)c);
                Assert.AreEqual(11, d);
                Assert.AreEqual(11, (long)e);
                Assert.AreEqual(0, f);
                Assert.AreEqual(0, (long)g);
                Assert.IsTrue(h);
            }
        }

        [Test]
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
                Assert.AreNotEqual(0, expected);
                var sum = (long)db.StringGet(key);
                Assert.AreEqual(expected, sum);
            }
        }

        private void Incr(IDatabase database, RedisKey key, int delta, ref int total)
        {
            database.StringIncrement(key, delta, CommandFlags.FireAndForget);
            total += delta;
        }

        [Test]
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
                Assert.AreEqual(3, count);
            }
        }
    }

}
