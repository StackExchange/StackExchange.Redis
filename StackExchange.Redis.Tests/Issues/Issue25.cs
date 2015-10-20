using System;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class Issue25 : TestBase
    {
        [Test]
        public void CaseInsensitive()
        {
            var options = ConfigurationOptions.Parse("ssl=true");
            Assert.IsTrue(options.Ssl);
            Assert.AreEqual("ssl=True", options.ToString());

            options = ConfigurationOptions.Parse("SSL=TRUE");
            Assert.IsTrue(options.Ssl);
            Assert.AreEqual("ssl=True", options.ToString());
        }

        [Test]
        public void UnkonwnKeywordHandling_Ignore()
        {
            var options = ConfigurationOptions.Parse("ssl2=true", true);
        }
        [Test] 
        //, ExpectedException(typeof(ArgumentException), ExpectedMessage = "Keyword 'ssl2' is not supported")]
        public void UnkonwnKeywordHandling_ExplicitFail()
        {
            Exception ex = Assert.Throws(typeof(ArgumentException), delegate {
                var options = ConfigurationOptions.Parse("ssl2=true", false);
            });
            Assert.That(ex.Message.Equals("Keyword 'ssl2' is not supported"));
        }
        [Test]
        //, ExpectedException(typeof(ArgumentException), ExpectedMessage = "Keyword 'ssl2' is not supported")]
        public void UnkonwnKeywordHandling_ImplicitFail()
        {
            Exception ex = Assert.Throws(typeof(ArgumentException), delegate {
                var options = ConfigurationOptions.Parse("ssl2=true");
            });
            Assert.That(ex.Message.Equals("Keyword 'ssl2' is not supported"));            
        }
    }
}
