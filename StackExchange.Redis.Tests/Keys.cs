using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Keys : TestBase
    {
        [Test]
        public void TestScan()
        {
            using(var muxer = Create(allowAdmin: true))
            {
                const int Database = 4;
                var db = muxer.GetDatabase(Database);
                GetServer(muxer).FlushDatabase(flags: CommandFlags.FireAndForget);

                const int Count = 1000;
                for (int i = 0; i < Count; i++)
                    db.StringSet("x" + i, "y" + i, flags: CommandFlags.FireAndForget);

                var count = GetServer(muxer).Keys(Database).Count();
                Assert.AreEqual(Count, count);
            }
        }
    }
}
