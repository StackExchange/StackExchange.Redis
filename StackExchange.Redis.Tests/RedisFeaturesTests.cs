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
            Assert.StartsWith("Features in 2.9" + Environment.NewLine, s);
            Assert.Contains("ExecAbort: True" + Environment.NewLine, s);

            features = new RedisFeatures(new Version(2, 9, 5));
            s = features.ToString();
            Assert.False(features.ExecAbort);
            Assert.StartsWith("Features in 2.9.5" + Environment.NewLine, s);
            Assert.Contains("ExecAbort: False" + Environment.NewLine, s);

            features = new RedisFeatures(new Version(3, 0));
            s = features.ToString();
            Assert.True(features.ExecAbort);
            Assert.StartsWith("Features in 3.0" + Environment.NewLine, s);
            Assert.Contains("ExecAbort: True" + Environment.NewLine, s);
        }
    }
}
