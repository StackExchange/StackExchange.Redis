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
        using var conn = await serverObj.ConnectAsync(withPubSub: false);
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

    [Theory]
    [InlineData(ProductVariant.Redis, ServerType.Standalone, true)]
    [InlineData(ProductVariant.Redis, ServerType.Cluster, false)]
    [InlineData(ProductVariant.Garnet, ServerType.Standalone, true)]
    [InlineData(ProductVariant.Garnet, ServerType.Cluster, false)]
    [InlineData(ProductVariant.Valkey, ServerType.Standalone, true)]
    [InlineData(ProductVariant.Valkey, ServerType.Cluster, true)]
    public async Task MultiDbSupportMatchesProductVariantAndServerType(ProductVariant variant, ServerType serverType, bool supportsMultiDb)
    {
        using var serverObj = new ProductServer(variant, log, serverType);
        await using var conn = await serverObj.ConnectAsync(withPubSub: false);

        var serverApi = conn.GetServer(conn.GetEndPoints().First());
        await serverApi.PingAsync();
        Assert.Equal(serverType, serverApi.ServerType);
        Assert.Equal(variant, serverApi.GetProductVariant(out _));

        RedisKey key = $"multidb:{variant}:{serverType}";
        const string db0Value = "db0";
        const string db1Value = "db1";
        var db0 = conn.GetDatabase(0);

        var db1 = conn.GetDatabase(1);

        await db0.StringSetAsync(key, db0Value);

        if (supportsMultiDb)
        {
            await db1.StringSetAsync(key, db1Value);
            Assert.Equal(db0Value, (string?)await db0.StringGetAsync(key));
            Assert.Equal(db1Value, (string?)await db1.StringGetAsync(key));
        }
        else
        {
            var ex = await Assert.ThrowsAsync<RedisConnectionException>(() => db1.StringSetAsync(key, db1Value));
            var inner = Assert.IsType<RedisCommandException>(ex.InnerException);
            Assert.Contains("cannot switch to database: 1", inner.Message);
            Assert.Equal(db0Value, (string?)await db0.StringGetAsync(key));
        }
    }

    private sealed class ProductServer : InProcessTestServer
    {
        private readonly ProductVariant _variant;

        public ProductServer(ProductVariant variant, ITestOutputHelper log, ServerType serverType = ServerType.Standalone)
            : base(log)
        {
            _variant = variant;
            ServerType = serverType;
        }

        protected override void Info(StringBuilder sb, string section)
        {
            base.Info(sb, section);
            if (section is "Server")
            {
                switch (_variant)
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

        protected override bool SupportMultiDb(out string err)
        {
            switch (_variant)
            {
                case ProductVariant.Valkey:
                    // support multiple databases even on cluster
                    err = "";
                    return true;
                default:
                    return base.SupportMultiDb(out err);
            }
        }
    }
}
