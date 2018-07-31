using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class BgSaveResponse : TestBase
    {
        public BgSaveResponse(ITestOutputHelper output) : base (output) { }

        [Theory (Skip = "We don't need to test this, and it really screws local testing hard.")]
        [InlineData(SaveType.BackgroundSave)]
        [InlineData(SaveType.BackgroundRewriteAppendOnlyFile)]
        public void ShouldntThrowException(SaveType saveType)
        {
            using (var conn = Create(null, null, true))
            {
                var Server = GetServer(conn);
                Server.Save(saveType);
            }
        }
    }
}
