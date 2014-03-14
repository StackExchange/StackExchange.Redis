using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class RealWorld
    {
        [Test]
        public void WhyDoesThisNotWork()
        {
            var sw = new StringWriter();
            Console.WriteLine("first:");
            using (var conn = ConnectionMultiplexer.Connect("localhost:6379,localhost:6380,name=Core (Q&A),tiebreaker=:RedisMaster,abortConnect=False", sw))
            {
                Console.WriteLine(sw);
                Console.WriteLine();
                Console.WriteLine("pausing...");
                Thread.Sleep(200);
                Console.WriteLine("second:");

                sw = new StringWriter();
                bool result = conn.Configure(sw);
                Console.WriteLine("Returned: {0}", result);
                Console.WriteLine(sw);
            }
            
        }
    }
}
