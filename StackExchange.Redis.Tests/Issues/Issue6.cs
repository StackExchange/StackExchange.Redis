using System;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class Issue6 :  TestBase
    {
        [Test]
        public void ShouldWorkWithoutEchoOrPing()
        {
            using(var conn = Create(proxy: Proxy.Twemproxy))
            {
                Console.WriteLine("config: " + conn.Configuration);
                var db = conn.GetDatabase();
                var time = db.Ping();
                Console.WriteLine("ping time: " + time);
            }
        }
    }
}
