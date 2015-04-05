using System;
using System.Threading;
using NUnit.Framework;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class SO27258984 : TestBase
    {
        [Test]
        public async Task Execute() {
            var ts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            using (var conn = Create()) {
                var db = conn.GetDatabase(0);
                await AddAsync(db, "test", "1");
                await db.KeyDeleteAsync("test");

                Assert.DoesNotThrow(() => Task.Run(() => AddAsync(db, "test", "1"), ts.Token).Wait(ts.Token));
            }
        }

        public async Task<bool> AddAsync(IDatabase db, string key, string value) {
            return await db.StringSetAsync(key, value, null, When.NotExists);
        }
    }
}
