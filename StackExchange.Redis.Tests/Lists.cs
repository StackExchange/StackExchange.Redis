using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Lists : TestBase
    {
        public Lists(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Ranges()
        {
            using (var conn = Create())
            {
                var db = conn.GetDatabase();
                RedisKey key = Me();

                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.ListRightPush(key, "abcdefghijklmnopqrstuvwxyz".Select(x => (RedisValue)x.ToString()).ToArray());

                Assert.Equal(26, db.ListLength(key));
                Assert.Equal("abcdefghijklmnopqrstuvwxyz", string.Concat(db.ListRange(key)));

                var last10 = db.ListRange(key, -10, -1);
                Assert.Equal("qrstuvwxyz", string.Concat(last10));
                db.ListTrim(key, 0, -11);

                Assert.Equal(16, db.ListLength(key));
                Assert.Equal("abcdefghijklmnop", string.Concat(db.ListRange(key)));
            }
        }
    }
}