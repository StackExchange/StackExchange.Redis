using Xunit;

namespace StackExchange.Redis.Tests;

public class HighIntegrityBasicOpsTests(ITestOutputHelper output, SharedConnectionFixture fixture) : BasicOpsTests(output, fixture)
{
    internal override bool HighIntegrity => true;
}

public class InProcHighIntegrityBasicOpsTests(ITestOutputHelper output, InProcServerFixture fixture) : InProcBasicOpsTests(output, fixture)
{
    internal override bool HighIntegrity => true;
}
