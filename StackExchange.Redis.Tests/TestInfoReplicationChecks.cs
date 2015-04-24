using System;
using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class TestInfoReplicationChecks : TestBase
    {
        [Test]
        public void Exec()
        {
            using(var conn = Create())
            {
                var parsed = ConfigurationOptions.Parse(conn.Configuration);
                Assert.AreEqual(5, parsed.ConfigCheckSeconds);
                var before = conn.GetCounters();
                Thread.Sleep(TimeSpan.FromSeconds(13));
                var after = conn.GetCounters();
                int done = (int)(after.Interactive.CompletedSynchronously - before.Interactive.CompletedSynchronously);
                Assert.IsTrue(done >= 2);
            }
        }
        protected override string GetConfiguration()
        {
            return base.GetConfiguration() + ",configCheckSeconds=5";
        }
    }
}
