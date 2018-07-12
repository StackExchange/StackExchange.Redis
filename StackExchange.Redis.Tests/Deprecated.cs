using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    /// <summary>
    /// Testing that things we depcreate still parse, but are otherwise defaults.
    /// </summary>
    public class Deprecated : TestBase
    {
        public Deprecated(ITestOutputHelper output) : base(output) { }

#pragma warning disable CS0618 // Type or member is obsolete
        [Fact]
        public void PreserveAsyncOrder()
        {
            Assert.True(Attribute.IsDefined(typeof(ConfigurationOptions).GetProperty(nameof(ConfigurationOptions.PreserveAsyncOrder)), typeof(ObsoleteAttribute)));

            var options = ConfigurationOptions.Parse("name=Hello");
            Assert.False(options.PreserveAsyncOrder);

            options = ConfigurationOptions.Parse("preserveAsyncOrder=true");
            Assert.Equal("", options.ToString());
            Assert.False(options.PreserveAsyncOrder);

            options = ConfigurationOptions.Parse("preserveAsyncOrder=false");
            Assert.Equal("", options.ToString());
            Assert.False(options.PreserveAsyncOrder);
        }

        [Fact]
        public void WriteBufferParse()
        {
            Assert.True(Attribute.IsDefined(typeof(ConfigurationOptions).GetProperty(nameof(ConfigurationOptions.WriteBuffer)), typeof(ObsoleteAttribute)));

            var options = ConfigurationOptions.Parse("name=Hello");
            Assert.Equal(0, options.WriteBuffer);

            options = ConfigurationOptions.Parse("writeBuffer=8092");
            Assert.Equal(0, options.WriteBuffer);
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
