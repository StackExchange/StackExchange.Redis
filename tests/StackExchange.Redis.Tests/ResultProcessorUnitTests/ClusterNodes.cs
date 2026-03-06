using System.Linq;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class ClusterNodes(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    // NOTE: ClusterNodesProcessor cannot be unit tested in isolation because it requires
    // a real PhysicalConnection with a bridge to parse the cluster configuration.
    // The processor calls connection.BridgeCouldBeNull which throws ObjectDisposedException
    // in the test environment. These tests are covered by integration tests instead.
}
