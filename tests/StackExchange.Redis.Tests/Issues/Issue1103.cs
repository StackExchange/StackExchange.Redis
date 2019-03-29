using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue1103 : TestBase
    {
        public Issue1103(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void LargeUInt64StoredCorrectly()
        {
            using (var muxer = Create())
            {
                var db = muxer.GetDatabase();
                const ulong expected = 142205255210238005;
                db.StringSet("foo", expected);
                var val = db.StringGet("foo");
                Log((string)val);
                var actual = (ulong)val;
                Assert.Equal(expected, actual);
            }
        }
    }
}
