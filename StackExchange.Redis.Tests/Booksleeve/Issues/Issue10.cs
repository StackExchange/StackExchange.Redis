using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve.Issues
{
    public class Issue10 : BookSleeveTestBase
    {
        public Issue10(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Execute()
        {
            using (var muxer = GetUnsecuredConnection())
            {
                const int DB = 5;
                const string Key = "issue-10-list";
                var conn = muxer.GetDatabase(DB);
                conn.KeyDeleteAsync(Key); // contents: nil
                conn.ListLeftPushAsync(Key, "abc"); // "abc"
                conn.ListLeftPushAsync(Key, "def"); // "def", "abc"
                conn.ListLeftPushAsync(Key, "ghi"); // "ghi", "def", "abc",
                conn.ListSetByIndexAsync(Key, 1, "jkl"); // "ghi", "jkl", "abc"

                var contents = conn.Wait(conn.ListRangeAsync(Key, 0, -1));
                Assert.Equal(3, contents.Length);
                Assert.Equal("ghi", contents[0]);
                Assert.Equal("jkl", contents[1]);
                Assert.Equal("abc", contents[2]);
            }
        }
    }
}
