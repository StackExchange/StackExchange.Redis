using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class BgSaveResponseTests(ITestOutputHelper output) : TestBase(output)
{
    [Theory(Skip = "We don't need to test this, and it really screws local testing hard.")]
    [InlineData(SaveType.BackgroundSave)]
    [InlineData(SaveType.BackgroundRewriteAppendOnlyFile)]
    public async Task ShouldntThrowException(SaveType saveType)
    {
        using var conn = Create(allowAdmin: true);

        var server = GetServer(conn);
        server.Save(saveType);
        await Task.Delay(1000);
    }
}
