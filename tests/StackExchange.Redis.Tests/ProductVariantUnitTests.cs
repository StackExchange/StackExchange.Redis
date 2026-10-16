using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class ProductVariantUnitTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(ProductVariant.Redis)]
    [InlineData(ProductVariant.Valkey)]
    [InlineData(ProductVariant.Garnet)]
    public async Task DetectProductVariant(ProductVariant variant)
    {
        using var serverObj = new ProductServer(variant, log);
        // with pub/sub, both connections issue HELLO, in either order relative to the interactive INFO
        using var conn = await serverObj.ConnectAsync(withPubSub: true);
        var serverApi = conn.GetServer(conn.GetEndPoints().First());
        serverApi.Ping();
        var reportedProduct = serverApi.GetProductVariant(out var reportedVersion);
        Assert.Equal(variant, reportedProduct);
        log.WriteLine($"Detected {reportedProduct} version: {reportedVersion}; redis API version: {serverApi.Version}");
        if (variant == ProductVariant.Redis)
        {
            Assert.Equal(serverObj.VersionString, reportedVersion);
        }
        else
        {
            Assert.Equal(ProductServer.VendorVersion, reportedVersion);
        }
        // the Redis API version must not be confused with the vendor's own version (#3278)
        Assert.Equal(serverObj.RedisVersion, serverApi.Version);
    }

    [Theory]
    [InlineData(ProductVariant.Redis)]
    [InlineData(ProductVariant.Valkey)]
    [InlineData(ProductVariant.Garnet)]
    public async Task DetectProductVariantFromHelloAlone(ProductVariant variant)
    {
        // with INFO disabled, HELLO is the only source of version data, so this pins down what we take from it:
        // "version" is the Redis API version only when "server" is redis (Redis, Garnet); Valkey repurposes it (#3278)
        using var serverObj = new ProductServer(variant, log);
        var config = serverObj.GetClientConfig(withPubSub: false);
        var commands = serverObj.GetCommands();
        commands.Remove(nameof(RedisCommand.INFO));
        config.CommandMap = CommandMap.Create(commands);
        var fallbackVersion = new Version(6, 0, 0); // HELLO exists from 6.0, and the fake server reports 7.0.0
        config.DefaultVersion = fallbackVersion;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var serverApi = conn.GetServer(conn.GetEndPoints().First());
        await serverApi.PingAsync();
        Assert.Equal(variant, serverApi.GetProductVariant(out var reportedVersion));
        log.WriteLine($"Detected {variant} version: {reportedVersion}; redis API version: {serverApi.Version}");
        switch (variant)
        {
            case ProductVariant.Redis:
                Assert.Equal(serverObj.RedisVersion, serverApi.Version);
                break;
            case ProductVariant.Garnet:
                Assert.Equal(serverObj.RedisVersion, serverApi.Version);
                Assert.Equal(ProductServer.VendorVersion, reportedVersion);
                break;
            case ProductVariant.Valkey:
                Assert.Equal(fallbackVersion, serverApi.Version); // HELLO told us nothing about the Redis API version
                Assert.Equal(ProductServer.VendorVersion, reportedVersion);
                break;
        }
    }

    [Fact]
    public async Task ValkeyVersionIsStableAcrossConnections()
    {
        // #3278: Valkey reports its own version as HELLO "version", so the detected Redis API version used to
        // depend on whether the (subscription) HELLO or the (interactive) INFO reply was processed last
        using var serverObj = new ProductServer(ProductVariant.Valkey, log);
        for (int i = 0; i < 10; i++)
        {
            await using var conn = await serverObj.ConnectAsync(withPubSub: true);
            var serverApi = conn.GetServer(conn.GetEndPoints().First());
            await serverApi.PingAsync();
            Assert.Equal(serverObj.RedisVersion, serverApi.Version);
            Assert.Equal(ProductVariant.Valkey, serverApi.GetProductVariant(out var reportedVersion));
            Assert.Equal(ProductServer.VendorVersion, reportedVersion);
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
        public const string VendorVersion = "9.1.2"; // must parse as a Version, or the HELLO-version confusion goes unnoticed

        private readonly ProductVariant _variant;

        public ProductServer(ProductVariant variant, ITestOutputHelper log, ServerType serverType = ServerType.Standalone)
            : base(log)
        {
            _variant = variant;
            ServerType = serverType;
        }

        protected override void Info(StringBuilder sb, string section, Server.RedisClient? client = null)
        {
            base.Info(sb, section, client);
            if (section is "Server")
            {
                switch (_variant)
                {
                    case ProductVariant.Garnet:
                        sb.AppendLine("garnet_version:" + VendorVersion)
                            .AppendLine("server_name:garnet");
                        break;
                    case ProductVariant.Valkey:
                        sb.AppendLine("valkey_version:" + VendorVersion)
                            .AppendLine("server_name:valkey");
                        break;
                }
            }
        }

        // HELLO shapes as measured against real servers (#3278):
        // Garnet 2.2.2: server => redis, version => 7.4.3 (the Redis API version), garnet_version => 2.2.2
        // Valkey 9.1.2: server => valkey, version => 9.1.2 (Valkey's own version; INFO has redis_version:7.2.4)
        protected override string HelloServerName => _variant switch
        {
            ProductVariant.Valkey => "valkey",
            _ => "redis",
        };

        protected override string HelloVersionString => _variant switch
        {
            ProductVariant.Valkey => VendorVersion,
            _ => VersionString,
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> HelloExtraFields => _variant switch
        {
            ProductVariant.Garnet => [new("garnet_version", VendorVersion)],
            _ => [],
        };

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
