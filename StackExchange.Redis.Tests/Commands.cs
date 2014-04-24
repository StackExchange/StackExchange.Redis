using System.Net;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Commands
    {
        [Test]
        public void Basic()
        {
            var config = ConfigurationOptions.Parse(".,$PING=p");
            Assert.AreEqual(1, config.EndPoints.Count);
            config.SetDefaultPorts();
            Assert.Contains(new DnsEndPoint(".",6379), config.EndPoints);
            var map = config.CommandMap;
            Assert.AreEqual("$PING=p", map.ToString());
            Assert.AreEqual(".:6379,$PING=p", config.ToString());
        }
    }
}
