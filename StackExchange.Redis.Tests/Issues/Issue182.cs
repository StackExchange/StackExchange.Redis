using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue182 : TestBase
    {
        protected override string GetConfiguration() => $"{TestConfig.Current.MasterServerAndPort},responseTimeout=10000";

        public Issue182(ITestOutputHelper output) : base (output) { }

        [FactLongRunning]
        public async Task SetMembers()
        {
            using (var conn = Create())
            {
                conn.ConnectionFailed += (s, a) =>
                {
                    Output.WriteLine(a.FailureType.ToString());
                    Output.WriteLine(a.Exception.Message);
                    Output.WriteLine(a.Exception.StackTrace);
                };
                var db = conn.GetDatabase();

                var key = Me();
                const int count = (int)5e6;
                var len = await db.SetLengthAsync(key);

                if (len != count)
                {
                    await db.KeyDeleteAsync(key);
                    foreach (var _ in Enumerable.Range(0, count))
                        db.SetAdd(key, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);

                    Assert.Equal(count, await db.SetLengthAsync(key)); // SCARD for set
                }
                var result = await db.SetMembersAsync(key);
                Assert.Equal(count, result.Length); // SMEMBERS result length
            }
        }

        [FactLongRunning]
        public async Task SetUnion()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();

                var key1 = Me() + ":1";
                var key2 = Me() + ":2";
                var dstkey = Me() + ":dst";

                const int count = (int)5e6;

                var len1 = await db.SetLengthAsync(key1);
                var len2 = await db.SetLengthAsync(key2);
                await db.KeyDeleteAsync(dstkey);

                if (len1 != count || len2 != count)
                {
                    await db.KeyDeleteAsync(key1);
                    await db.KeyDeleteAsync(key2);
                    

                    foreach (var _ in Enumerable.Range(0, count))
                    {
                        db.SetAdd(key1, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);
                        db.SetAdd(key2, Guid.NewGuid().ToByteArray(), CommandFlags.FireAndForget);
                    }
                    Assert.Equal(count, await db.SetLengthAsync(key1)); // SCARD for set 1
                    Assert.Equal(count, await db.SetLengthAsync(key2)); // SCARD for set 2
                }
                await db.SetCombineAndStoreAsync(SetOperation.Union, dstkey, key1, key2);
                var dstLen = db.SetLength(dstkey);
                Assert.Equal(count * 2, dstLen); // SCARD for destination set
            }
        }
    }
}
