using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve.Issues
{
    public class SO11766033 : BookSleeveTestBase
    {
        public SO11766033(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestNullString()
        {
            using (var muxer = GetUnsecuredConnection(true))
            {
                var redis = muxer.GetDatabase();
                const string expectedTestValue = null;
                var uid = Me();
                redis.StringSetAsync(uid, "abc");
                redis.StringSetAsync(uid, expectedTestValue);
                string testValue = redis.StringGet(uid);
                Assert.Null(testValue);
            }
        }

        [Fact]
        public void TestEmptyString()
        {
            using (var muxer = GetUnsecuredConnection(true))
            {
                var redis = muxer.GetDatabase();
                const string expectedTestValue = "";
                var uid = Me();

                redis.StringSetAsync(uid, expectedTestValue);
                string testValue = redis.StringGet(uid);

                Assert.Equal(expectedTestValue, testValue);
            }
        }
    }
}
