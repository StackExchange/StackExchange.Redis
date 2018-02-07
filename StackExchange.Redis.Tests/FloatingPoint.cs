using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class FloatingPoint : TestBase
    {
        public FloatingPoint(ITestOutputHelper output) : base (output) { }

        private static bool Within(double x, double y, double delta)
        {
            return Math.Abs(x - y) <= delta;
        }

        [Fact]
        public void IncrDecrFloatingPoint()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                double[] incr =
                {
                    12.134,
                    -14561.0000002,
                    125.3421,
                    -2.49892498
                }, decr =
                {
                    99.312,
                    12,
                    -35
                };
                double sum = 0;
                foreach (var value in incr)
                {
                    db.StringIncrement(key, value, CommandFlags.FireAndForget);
                    sum += value;
                }
                foreach (var value in decr)
                {
                    db.StringDecrement(key, value, CommandFlags.FireAndForget);
                    sum -= value;
                }
                var val = (double)db.StringGet(key);

                Assert.True(Within(sum, val, 0.0001));
            }
        }

        [Fact]
        public async Task IncrDecrFloatingPointAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                double[] incr =
                {
                    12.134,
                    -14561.0000002,
                    125.3421,
                    -2.49892498
                }, decr =
                {
                    99.312,
                    12,
                    -35
                };
                double sum = 0;
                foreach (var value in incr)
                {
                    await db.StringIncrementAsync(key, value).ForAwait();
                    sum += value;
                }
                foreach (var value in decr)
                {
                    await db.StringDecrementAsync(key, value).ForAwait();
                    sum -= value;
                }
                var val = (double)await db.StringGetAsync(key).ForAwait();

                Assert.True(Within(sum, val, 0.0001));
            }
        }

        [Fact]
        public void HashIncrDecrFloatingPoint()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                RedisValue field = "foo";
                db.KeyDelete(key, CommandFlags.FireAndForget);
                double[] incr =
                {
                    12.134,
                    -14561.0000002,
                    125.3421,
                    -2.49892498
                }, decr =
                {
                    99.312,
                    12,
                    -35
                };
                double sum = 0;
                foreach (var value in incr)
                {
                    db.HashIncrement(key, field, value, CommandFlags.FireAndForget);
                    sum += value;
                }
                foreach (var value in decr)
                {
                    db.HashDecrement(key, field, value, CommandFlags.FireAndForget);
                    sum -= value;
                }
                var val = (double)db.HashGet(key, field);

                Assert.True(Within(sum, val, 0.0001));
            }
        }

        [Fact]
        public async Task HashIncrDecrFloatingPointAsync()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();
                RedisValue field = "bar";
                db.KeyDelete(key, CommandFlags.FireAndForget);
                double[] incr =
                {
                    12.134,
                    -14561.0000002,
                    125.3421,
                    -2.49892498
                }, decr =
                {
                    99.312,
                    12,
                    -35
                };
                double sum = 0;
                foreach (var value in incr)
                {
                    await db.HashIncrementAsync(key, field, value).ForAwait();
                    sum += value;
                }
                foreach (var value in decr)
                {
                    await db.HashDecrementAsync(key, field, value).ForAwait();
                    sum -= value;
                }
                var val = (double)await db.HashGetAsync(key, field).ForAwait();

                Assert.True(Within(sum, val, 0.0001));
            }
        }
    }
}