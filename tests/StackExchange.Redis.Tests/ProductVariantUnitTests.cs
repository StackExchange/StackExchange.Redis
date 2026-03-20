using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ProductVariantUnitTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(ProductVariant.Redis)]
    [InlineData(ProductVariant.Valkey)]
    [InlineData(ProductVariant.Garnet)]
    public async Task DetectProductVariant(ProductVariant variant)
    {
        using var serverObj = new ProductServer(variant, log);
        using var conn = await serverObj.ConnectAsync();
        var serverApi = conn.GetServer(conn.GetEndPoints().First());
        serverApi.Ping();
        var reportedProduct = serverApi.GetProductVariant(out var reportedVersion);
        Assert.Equal(variant, reportedProduct);
        log.WriteLine($"Detected {reportedProduct} version: {reportedVersion}");
        if (variant == ProductVariant.Redis)
        {
            Assert.Equal(serverObj.VersionString, reportedVersion);
        }
        else
        {
            Assert.Equal("1.2.3-preview4", reportedVersion);
        }
    }

    private sealed class ProductServer(ProductVariant variant, ITestOutputHelper log) : InProcessTestServer(log)
    {
        protected override void Info(StringBuilder sb, string section)
        {
            base.Info(sb, section);
            if (section is "Server")
            {
                switch (variant)
                {
                    case ProductVariant.Garnet:
                        sb.AppendLine("garnet_version:1.2.3-preview4");
                        break;
                    case ProductVariant.Valkey:
                        sb.AppendLine("valkey_version:1.2.3-preview4")
                            .AppendLine("server_name:valkey");
                        break;
                }
            }
        }
    }
}
