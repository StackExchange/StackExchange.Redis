using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SocketTests(ITestOutputHelper output) : TestBase(output)
{
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort;

    [Fact]
    public async Task CheckForSocketLeaks()
    {
        Skip.UnlessLongRunning();
        const int count = 2000;
        for (var i = 0; i < count; i++)
        {
            await using var _ = Create(clientName: "Test: " + i);
            // Intentionally just creating and disposing to leak sockets here
            // ...so we can figure out what's happening.
        }
        // Force GC before memory dump in debug below...
        CollectGarbage();

        if (Debugger.IsAttached)
        {
            Debugger.Break();
        }
    }
}
