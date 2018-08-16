using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue10 : TestBase
    {
        public Issue10(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Execute()
        {
            using (var muxer = Create())
            {
                var key = Me();
                var conn = muxer.GetDatabase();
                conn.KeyDeleteAsync(key); // contents: nil
                conn.ListLeftPushAsync(key, "abc"); // "abc"
                conn.ListLeftPushAsync(key, "def"); // "def", "abc"
                conn.ListLeftPushAsync(key, "ghi"); // "ghi", "def", "abc",
                conn.ListSetByIndexAsync(key, 1, "jkl"); // "ghi", "jkl", "abc"

                var contents = conn.Wait(conn.ListRangeAsync(key, 0, -1));
                Assert.Equal(3, contents.Length);
                Assert.Equal("ghi", contents[0]);
                Assert.Equal("jkl", contents[1]);
                Assert.Equal("abc", contents[2]);
            }
        }
    }
}
