using System.Diagnostics;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Sockets : TestBase
    {
        protected override string GetConfiguration() => TestConfig.Current.MasterServerAndPort;
        public Sockets(ITestOutputHelper output) : base (output) { }

        [FactLongRunning]
        public void CheckForSocketLeaks()
        {
            const int count = 2000;
            for (var i = 0; i < count; i++)
            {
                using (var _ = Create(clientName: "Test: " + i))
                {
                    // Intentionally just creating and disposing to leak sockets here
                    // ...so we can figure out what's happening.
                }
            }
            // Force GC before memory dump in debug below...
            CollectGarbage();

            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
        }
    }
}
