using System;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class RedisFeaturesTests
    {
        [Fact]
        public void ExecAbort() // a random one because it is fun
        {
            var features = new RedisFeatures(new Version(2, 9));
            var s = features.ToString();
            Assert.True(features.ExecAbort);
            Assert.StartsWith("Features in 2.9\r\n", s);
            Assert.Contains("ExecAbort: True\r\n", s);

            features = new RedisFeatures(new Version(2, 9, 5));
            s = features.ToString();
            Assert.False(features.ExecAbort);
            Assert.StartsWith("Features in 2.9.5\r\n", s);
            Assert.Contains("ExecAbort: False\r\n", s);

            features = new RedisFeatures(new Version(3, 0));
            s = features.ToString();
            Assert.True(features.ExecAbort);
            Assert.StartsWith("Features in 3.0\r\n", s);
            Assert.Contains("ExecAbort: True\r\n", s);
        }
    }
}
