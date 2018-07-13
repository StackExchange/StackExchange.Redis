using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class SO11766033 : TestBase
    {
        public SO11766033(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestNullString()
        {
            using (var muxer = Create())
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
            using (var muxer = Create())
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
