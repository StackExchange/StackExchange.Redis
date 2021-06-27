using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class SO10825542 : TestBase
    {
        public SO10825542(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task Execute()
        {
            using (var muxer = Create())
            {
                var key = Me();

                var con = muxer.GetDatabase();
                // set the field value and expiration
                _ = con.HashSetAsync(key, "field1", Encoding.UTF8.GetBytes("hello world"));
                _ = con.KeyExpireAsync(key, TimeSpan.FromSeconds(7200));
                _ = con.HashSetAsync(key, "field2", "fooobar");
                var result = await con.HashGetAllAsync(key).ForAwait();

                Assert.Equal(2, result.Length);
                var dict = result.ToStringDictionary();
                Assert.Equal("hello world", dict["field1"]);
                Assert.Equal("fooobar", dict["field2"]);
            }
        }
    }
}
