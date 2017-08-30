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
            const int db = 3;
            using (var muxer = GetUnsecuredConnection(true))
            {
                var redis = muxer.GetDatabase(db);
                const string expectedTestValue = null;
                var uid = CreateUniqueName();
                redis.StringSetAsync(uid, "abc");
                redis.StringSetAsync(uid, expectedTestValue);
                string testValue = redis.StringGet(uid);
                Assert.Null(testValue);
            }
        }

        [Fact]
        public void TestEmptyString()
        {
            const int db = 3;
            using (var muxer = GetUnsecuredConnection(true))
            {
                var redis = muxer.GetDatabase(db);
                const string expectedTestValue = "";
                var uid = CreateUniqueName();

                redis.StringSetAsync(uid, expectedTestValue);
                string testValue = redis.StringGet(uid);

                Assert.Equal(expectedTestValue, testValue);
            }
        }
    }
}
