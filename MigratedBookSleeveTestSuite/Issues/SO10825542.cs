using System;
using System.Text;
using NUnit.Framework;
using StackExchange.Redis;

namespace Tests.Issues
{
    [TestFixture]
    public class SO10825542
    {
        [Test]
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

                Assert.AreEqual(2, task.Result.Length);
                var dict = task.Result.ToStringDictionary();
                Assert.AreEqual("hello world", dict["field1"]);
                Assert.AreEqual("fooobar", dict["field2"]);
            }
        }
    }
}
