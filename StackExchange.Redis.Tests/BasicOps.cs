using System;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using Xunit.Abstractions;
#if DEBUG
using System.Diagnostics;
using System.Threading;
#endif

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
        public async Task GetWithExpiryWrongTypeAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key);
                db.SetAdd(key, "abc");
                var ex = await Assert.ThrowsAsync<RedisServerException>(async () =>
                {
                    try
                    {
                        Output.WriteLine("Key: " + (string)key);
                        var async = await db.StringGetWithExpiryAsync(key).ForAwait();
                    }
                    catch (AggregateException e)
                    {
                        throw e.InnerExceptions[0];
                    }
                }).ForAwait();
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
                await Task.Delay(2000).ForAwait();
                Debug.WriteLine("Pinging...");
                Assert.Equal(key, db.StringGet(key));
            }
        }
#endif

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task IncrAsync(bool preserveOrder)
        {
            using (var muxer = Create())
            {
                muxer.PreserveAsyncOrder = preserveOrder;
                var conn = muxer.GetDatabase();
                RedisKey key = Me();
                conn.KeyDelete(key, CommandFlags.FireAndForget);
                var nix = conn.KeyExistsAsync(key).ForAwait();
                var a = conn.StringGetAsync(key).ForAwait();
                var b = conn.StringIncrementAsync(key).ForAwait();
                var c = conn.StringGetAsync(key).ForAwait();
                var d = conn.StringIncrementAsync(key, 10).ForAwait();
                var e = conn.StringGetAsync(key).ForAwait();
                var f = conn.StringDecrementAsync(key, 11).ForAwait();
                var g = conn.StringGetAsync(key).ForAwait();
                var h = conn.KeyExistsAsync(key).ForAwait();
                Assert.False(await nix);
                Assert.True((await a).IsNull);
                Assert.Equal(0, (long)(await a));
                Assert.Equal(1, await b);
                Assert.Equal(1, (long)(await c));
                Assert.Equal(11, await d);
                Assert.Equal(11, (long)(await e));
                Assert.Equal(0, await f);
                Assert.Equal(0, (long)(await g));
                Assert.True(await h);
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
