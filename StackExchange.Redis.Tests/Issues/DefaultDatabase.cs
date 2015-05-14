using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using StackExchange.Redis;

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
                using (var conn = ConnectionMultiplexer.Connect(string.Format("{0}:{1}", PrimaryServer, PrimaryPort), log)) {
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
                using (var conn = ConnectionMultiplexer.Connect(string.Format("{0}:{1},defaultDatabase=3", PrimaryServer, PrimaryPort), log)) {
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
