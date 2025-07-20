using Xunit;

namespace StackExchange.Redis.Tests;

public class HighIntegrityBasicOpsTests(ITestOutputHelper output, SharedConnectionFixture fixture) : BasicOpsTests(output, fixture)
{
    internal override bool HighIntegrity => true;
}
