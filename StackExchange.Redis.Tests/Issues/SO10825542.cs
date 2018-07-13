using System;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class SO10825542 : TestBase
    {
        public SO10825542(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Execute()
        {
            using (var muxer = Create())
            {
                var key = Me();

                var con = muxer.GetDatabase();
                // set the field value and expiration
                con.HashSetAsync(key, "field1", Encoding.UTF8.GetBytes("hello world"));
                con.KeyExpireAsync(key, TimeSpan.FromSeconds(7200));
                con.HashSetAsync(key, "field2", "fooobar");
                var task = con.HashGetAllAsync(key);
                con.Wait(task);

                Assert.Equal(2, task.Result.Length);
                var dict = task.Result.ToStringDictionary();
                Assert.Equal("hello world", dict["field1"]);
                Assert.Equal("fooobar", dict["field2"]);
            }
        }
    }
}
