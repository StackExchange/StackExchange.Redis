using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues;

public class BgSaveResponseTests : TestBase
{
    public BgSaveResponseTests(ITestOutputHelper output) : base (output) { }

    [Theory (Skip = "We don't need to test this, and it really screws local testing hard.")]
    [InlineData(SaveType.BackgroundSave)]
    [InlineData(SaveType.BackgroundRewriteAppendOnlyFile)]
    public async Task ShouldntThrowException(SaveType saveType)
    {
        using var conn = Create(allowAdmin: true);

        var Server = GetServer(conn);
        Server.Save(saveType);
        await Task.Delay(1000);
    }
}
