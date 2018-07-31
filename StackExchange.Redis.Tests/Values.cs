﻿using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Values : TestBase
    {
        public Values(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void NullValueChecks()
        {
            RedisValue four = 4;
            Assert.False(four.IsNull);
            Assert.True(four.IsInteger);
            Assert.True(four.HasValue);
            Assert.False(four.IsNullOrEmpty);

            RedisValue n = default(RedisValue);
            Assert.True(n.IsNull);
            Assert.False(n.IsInteger);
            Assert.False(n.HasValue);
            Assert.True(n.IsNullOrEmpty);

            RedisValue emptyArr = new byte[0];
            Assert.False(emptyArr.IsNull);
            Assert.False(emptyArr.IsInteger);
            Assert.False(emptyArr.HasValue);
            Assert.True(emptyArr.IsNullOrEmpty);
        }
    }
}
