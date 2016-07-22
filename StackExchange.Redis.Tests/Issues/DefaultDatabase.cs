using System;
using System.IO;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
	[TestFixture]
	public class DefaultDatabase : TestBase
	{
		[Test]
		public void UnspecifiedDbId_ReturnsNull()
		{
			var config = ConfigurationOptions.Parse("localhost");
			Assert.IsNull(config.DefaultDatabase);
		}

		[Test]
		public void SpecifiedDbId_ReturnsExpected()
		{
			var config = ConfigurationOptions.Parse("localhost,defaultDatabase=3");
			Assert.AreEqual(3, config.DefaultDatabase);
		}

        [Test]
        public void ConfigurationOptions_UnspecifiedDefaultDb()
        {
            var log = new StringWriter();
            try
            {
                using (var conn = ConnectionMultiplexer.Connect($"{PrimaryServer}:{PrimaryPort}", log)) {
                    var db = conn.GetDatabase();
                    Assert.AreEqual(0, db.Database);
                }
            }
            finally
            {
                Console.WriteLine(log);
            }
        }

        [Test]
        public void ConfigurationOptions_SpecifiedDefaultDb()
        {
            var log = new StringWriter();
            try
            {
                using (var conn = ConnectionMultiplexer.Connect($"{PrimaryServer}:{PrimaryPort},defaultDatabase=3", log)) {
                    var db = conn.GetDatabase();
                    Assert.AreEqual(3, db.Database);
                }
            }
            finally
            {
                Console.WriteLine(log);
            }
        }
	}
}
