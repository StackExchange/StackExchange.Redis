using System;
using System.Text;
using StackExchange.Redis;
using Xunit;

namespace Tests.Issues
{
    public class SO10825542
    {
        [Fact]
        public void Execute()
        {
            using (var muxer = Config.GetUnsecuredConnection())
            {
                var key = "somekey1";

                var con = muxer.GetDatabase(1);
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
