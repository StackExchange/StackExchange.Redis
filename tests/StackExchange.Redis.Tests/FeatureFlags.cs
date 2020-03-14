using Xunit;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class FeatureFlags
    {
        [Fact]
        public void UnknownFlagToggle()
        {
            Assert.False(ConnectionMultiplexer.GetFeatureFlag("nope"));
            ConnectionMultiplexer.SetFeatureFlag("nope", true);
            Assert.False(ConnectionMultiplexer.GetFeatureFlag("nope"));
        }

        [Fact]
        public void KnownFlagToggle()
        {
            Assert.False(ConnectionMultiplexer.GetFeatureFlag("preventthreadtheft"));
            ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", true);
            Assert.True(ConnectionMultiplexer.GetFeatureFlag("preventthreadtheft"));
            ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", false);
            Assert.False(ConnectionMultiplexer.GetFeatureFlag("preventthreadtheft"));
        }
    }
}
