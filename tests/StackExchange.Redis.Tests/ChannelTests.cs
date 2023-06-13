using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class ChannelTests
    {
        [Fact]
        public void UseImplicitAutoPattern_OnByDefault()
        {
            Assert.True(RedisChannel.UseImplicitAutoPattern);
        }

        [Theory]
        [InlineData("abc", true, false)]
        [InlineData("abc*def", true, true)]
        [InlineData("abc", false, false)]
        [InlineData("abc*def", false, false)]
        public void ValidateAutoPatternModeString(string name, bool useImplicitAutoPattern, bool isPatternBased)
        {
            bool oldValue = RedisChannel.UseImplicitAutoPattern;
            try
            {
                RedisChannel.UseImplicitAutoPattern = useImplicitAutoPattern;
                RedisChannel channel = name;
                Assert.Equal(isPatternBased, channel.IsPatternBased);
            }
            finally
            {
                RedisChannel.UseImplicitAutoPattern = oldValue;
            }
        }

        [Theory]
        [InlineData("abc", RedisChannel.PatternMode.Auto, true, false)]
        [InlineData("abc*def", RedisChannel.PatternMode.Auto, true, true)]
        [InlineData("abc", RedisChannel.PatternMode.Literal, true, false)]
        [InlineData("abc*def", RedisChannel.PatternMode.Literal, true, false)]
        [InlineData("abc", RedisChannel.PatternMode.Pattern, true, true)]
        [InlineData("abc*def", RedisChannel.PatternMode.Pattern, true, true)]
        [InlineData("abc", RedisChannel.PatternMode.Auto, false, false)]
        [InlineData("abc*def", RedisChannel.PatternMode.Auto, false, true)]
        [InlineData("abc", RedisChannel.PatternMode.Literal, false, false)]
        [InlineData("abc*def", RedisChannel.PatternMode.Literal, false, false)]
        [InlineData("abc", RedisChannel.PatternMode.Pattern, false, true)]
        [InlineData("abc*def", RedisChannel.PatternMode.Pattern, false, true)]
        public void ValidateModeSpecifiedIgnoresGlobalSetting(string name, RedisChannel.PatternMode mode, bool useImplicitAutoPattern, bool isPatternBased)
        {
            bool oldValue = RedisChannel.UseImplicitAutoPattern;
            try
            {
                RedisChannel.UseImplicitAutoPattern = useImplicitAutoPattern;
                RedisChannel channel = new(name, mode);
                Assert.Equal(isPatternBased, channel.IsPatternBased);
            }
            finally
            {
                RedisChannel.UseImplicitAutoPattern = oldValue;
            }
        }

        [Theory]
        [InlineData("abc", true, false)]
        [InlineData("abc*def", true, true)]
        [InlineData("abc", false, false)]
        [InlineData("abc*def", false, false)]
        public void ValidateAutoPatternModeBytes(string name, bool useImplicitAutoPattern, bool isPatternBased)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            bool oldValue = RedisChannel.UseImplicitAutoPattern;
            try
            {
                RedisChannel.UseImplicitAutoPattern = useImplicitAutoPattern;
                RedisChannel channel = bytes;
                Assert.Equal(isPatternBased, channel.IsPatternBased);
            }
            finally
            {
                RedisChannel.UseImplicitAutoPattern = oldValue;
            }
        }

        [Theory]
        [InlineData("abc", RedisChannel.PatternMode.Auto, true, false)]
        [InlineData("abc*def", RedisChannel.PatternMode.Auto, true, true)]
        [InlineData("abc", RedisChannel.PatternMode.Literal, true, false)]
        [InlineData("abc*def", RedisChannel.PatternMode.Literal, true, false)]
        [InlineData("abc", RedisChannel.PatternMode.Pattern, true, true)]
        [InlineData("abc*def", RedisChannel.PatternMode.Pattern, true, true)]
        [InlineData("abc", RedisChannel.PatternMode.Auto, false, false)]
        [InlineData("abc*def", RedisChannel.PatternMode.Auto, false, true)]
        [InlineData("abc", RedisChannel.PatternMode.Literal, false, false)]
        [InlineData("abc*def", RedisChannel.PatternMode.Literal, false, false)]
        [InlineData("abc", RedisChannel.PatternMode.Pattern, false, true)]
        [InlineData("abc*def", RedisChannel.PatternMode.Pattern, false, true)]
        public void ValidateModeSpecifiedIgnoresGlobalSettingBytes(string name, RedisChannel.PatternMode mode, bool useImplicitAutoPattern, bool isPatternBased)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            bool oldValue = RedisChannel.UseImplicitAutoPattern;
            try
            {
                RedisChannel.UseImplicitAutoPattern = useImplicitAutoPattern;
                RedisChannel channel = new(bytes, mode);
                Assert.Equal(isPatternBased, channel.IsPatternBased);
            }
            finally
            {
                RedisChannel.UseImplicitAutoPattern = oldValue;
            }
        }
    }
}
