using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    [TestFixture]
    public class BgSaveResponse : TestBase
    {
        [Test]
        public void ShouldntThrowException()
        {
            using (var conn = Create(null, null, true))
            {
                var Server = GetServer(conn);
                Server.Save(SaveType.BackgroundSave);
            }
        }
    }
}
