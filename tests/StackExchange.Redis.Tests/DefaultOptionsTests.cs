using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis.Configuration;
using Xunit;

namespace StackExchange.Redis.Tests;

public class DefaultOptionsTests(ITestOutputHelper output) : TestBase(output)
{
    public class TestOptionsProvider(string domainSuffix) : DefaultOptionsProvider
    {
        private readonly string _domainSuffix = domainSuffix;

        public override bool AbortOnConnectFail => true;
        public override TimeSpan? ConnectTimeout => TimeSpan.FromSeconds(123);
        public override bool AllowAdmin => true;
        public override BacklogPolicy BacklogPolicy => BacklogPolicy.FailFast;
        public override bool CheckCertificateRevocation => true;
        public override CommandMap CommandMap => CommandMap.Create(new HashSet<string>() { "SELECT" });
        public override TimeSpan ConfigCheckInterval => TimeSpan.FromSeconds(124);
        public override string ConfigurationChannel => "TestConfigChannel";
        public override int ConnectRetry => 123;
        public override Version DefaultVersion => new Version(1, 2, 3, 4);
        protected override string GetDefaultClientName() => "TestPrefix-" + base.GetDefaultClientName();
        public override bool HeartbeatConsistencyChecks => true;
        public override TimeSpan HeartbeatInterval => TimeSpan.FromMilliseconds(500);
        public override bool IsMatch(EndPoint endpoint) => endpoint is DnsEndPoint dnsep && dnsep.Host.EndsWith(_domainSuffix);
        public override TimeSpan KeepAliveInterval => TimeSpan.FromSeconds(125);
        public override ILoggerFactory? LoggerFactory => NullLoggerFactory.Instance;
        public override Proxy Proxy => Proxy.Twemproxy;
        public override IReconnectRetryPolicy ReconnectRetryPolicy => new TestRetryPolicy();
        public override bool ResolveDns => true;
        public override TimeSpan SyncTimeout => TimeSpan.FromSeconds(126);
        public override string TieBreaker => "TestTiebreaker";
        public override string? User => "TestUser";
        public override string? Password => "TestPassword";
    }

    public class TestRetryPolicy : IReconnectRetryPolicy
    {
        public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry) => false;
    }

    [Fact]
    public void IsMatchOnDomain()
    {
        DefaultOptionsProvider.AddProvider(new TestOptionsProvider(".testdomain"));

        var epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint("local.testdomain", 0) });
        var provider = DefaultOptionsProvider.GetProvider(epc);
        Assert.IsType<TestOptionsProvider>(provider);

        epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint("local.nottestdomain", 0) });
        provider = DefaultOptionsProvider.GetProvider(epc);
        Assert.IsType<DefaultOptionsProvider>(provider);
    }

    [Theory]
    [InlineData("contoso.redis.cache.windows.net")]
    [InlineData("contoso.REDIS.CACHE.chinacloudapi.cn")] // added a few upper case chars to validate comparison
    [InlineData("contoso.redis.cache.usgovcloudapi.net")]
    [InlineData("contoso.redis.cache.sovcloud-api.de")]
    [InlineData("contoso.redis.cache.sovcloud-api.fr")]
    public void IsMatchOnAzureDomain(string hostName)
    {
        var epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint(hostName, 0) });
        var provider = DefaultOptionsProvider.GetProvider(epc);
        Assert.IsType<AzureOptionsProvider>(provider);
    }

    [Theory]
    [InlineData("contoso.redis.azure.net")]
    [InlineData("contoso.redis.chinacloudapi.cn")]
    [InlineData("contoso.redis.usgovcloudapi.net")]
    [InlineData("contoso.redisenterprise.cache.azure.net")]
    public void IsMatchOnAzureManagedRedisDomain(string hostName)
    {
        var epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint(hostName, 0) });
        var provider = DefaultOptionsProvider.GetProvider(epc);
        Assert.IsType<AzureManagedRedisOptionsProvider>(provider);
    }

    [Theory]
    [InlineData("amr", typeof(AzureManagedRedisOptionsProvider))]
    [InlineData("AMR", typeof(AzureManagedRedisOptionsProvider))] // names are case-insensitive, like every other key
    [InlineData("azure", typeof(AzureOptionsProvider))]
    [InlineData("rediscloud", typeof(RedisCloudOptionsProvider))]
    [InlineData("enterprise", typeof(RedisEnterpriseOptionsProvider))]
    public void DefaultsProviderCanBeNamedInAConfigurationString(string name, Type expected)
    {
        // the on-premise case is why this exists: an Enterprise cluster has whatever DNS its operator gave it,
        // so IsMatch can never recognize it, and until now the only way to select a provider was to write code
        var options = ConfigurationOptions.Parse($"localhost,defaults={name}");
        Assert.IsType(expected, options.Defaults);

        // and it round-trips, because it was chosen rather than inferred
        var text = options.ToString();
        Output.WriteLine(text);
        Assert.Contains($"defaults={name.ToLowerInvariant()}", text);
        Assert.IsType(expected, ConfigurationOptions.Parse(text).Defaults);
    }

    [Fact]
    public void ProviderToStringPrefersTheName()
    {
        // for logs: "amr" rather than a namespace-qualified type name
        Assert.Equal("amr", new AzureManagedRedisOptionsProvider().ToString());
        Assert.Equal("enterprise", new RedisEnterpriseOptionsProvider().ToString());

        // ...and an unnameable one still says something useful, which is exactly why serialization tests
        // Name rather than ToString: this is never null
        var custom = new TestOptionsProvider(".custom");
        Assert.Null(custom.Name);
        Assert.Contains(nameof(TestOptionsProvider), custom.ToString());
    }

    [Fact]
    public void UnknownDefaultsProviderNameIsRejectedWithTheAlternatives()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationOptions.Parse("localhost,defaults=sideways"));
        Output.WriteLine(ex.Message);
        Assert.Contains("enterprise", ex.Message); // the message lists what it would have accepted
        Assert.Contains("rediscloud", ex.Message);
    }

    [Fact]
    public void AnInferredDefaultsProviderIsNotSerialized()
    {
        // the trap this guards: the Defaults getter memoizes an inferred provider into the same field an
        // explicit set writes, so without tracking *how* it got there, merely reading the property would make
        // an endpoint-derived guess look like a decision - and re-parsing the string would then pin it
        var options = ConfigurationOptions.Parse("contoso.cloud.redislabs.com");
        Assert.IsType<RedisCloudOptionsProvider>(options.Defaults); // inferred, and memoized by that read

        Assert.DoesNotContain("defaults=", options.ToString());
        Assert.DoesNotContain("defaults=", options.Clone().ToString());
    }

    [Fact]
    public void AnExplicitDefaultsProviderSurvivesCloning()
    {
        var options = ConfigurationOptions.Parse("localhost,defaults=enterprise");
        var clone = options.Clone();

        Assert.IsType<RedisEnterpriseOptionsProvider>(clone.Defaults);
        Assert.Contains("defaults=enterprise", clone.ToString());
    }

    [Fact]
    public void AnUnnameableProviderIsNotSerializedEvenWhenExplicit()
    {
        // a custom provider is still perfectly usable in code; it just cannot be expressed as a string, the
        // same way an inbuilt tunnel can be and a custom one cannot
        var options = ConfigurationOptions.Parse("localhost");
        options.Defaults = new TestOptionsProvider(".unnameable");

        Assert.Null(options.Defaults.Name);
        Assert.DoesNotContain("defaults=", options.ToString());
    }

    [Theory]
    [InlineData("redis-12345.c1.eu-west-1-2.ec2.cloud.redislabs.com")]
    [InlineData("contoso.CLOUD.REDISLABS.COM")] // case-insensitive, as the sibling providers are
    [InlineData("redis-12345.c1.us-east-1-2.ec2.cloud.redis.io")] // newer routing scheme
    [InlineData("contoso.redislabs.com")] // older subscriptions, addressed directly
    public void IsMatchOnRedisCloudDomain(string hostName)
    {
        var epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint(hostName, 0) });
        var provider = DefaultOptionsProvider.GetProvider(epc);
        Assert.IsType<RedisCloudOptionsProvider>(provider);
    }

    [Fact]
    public void RedisCloudDoesNotInheritTheAzureManagedAssumptions()
    {
        // the two deployments look similar and are not: AMR is TLS-only with a 7.4 floor, Redis Cloud is
        // neither, so copying those two would break plaintext databases and over-claim the server version
        var epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint("contoso.cloud.redislabs.com", 0) });
        var cloud = DefaultOptionsProvider.GetProvider(epc);
        var amr = new AzureManagedRedisOptionsProvider();

        Assert.True(amr.GetDefaultSsl(epc));
        Assert.False(cloud.GetDefaultSsl(epc));
        Assert.Equal(RedisFeatures.v7_4_0, amr.DefaultVersion);
        Assert.Equal(DefaultOptionsProvider.BaseDefaultVersion, cloud.DefaultVersion);

        // ...but it does share the parts that are about being a proxied, hosted deployment
        Assert.Equal(RedisProtocol.Resp3, cloud.Protocol);
        Assert.Equal("", cloud.ConfigurationChannel);
        Assert.False(cloud.AbortOnConnectFail);
    }

    [Theory]
    [InlineData("contoso.redis.azure.net", MaintenanceNotificationMode.Auto)] // AMR asks, pre-emptively
    [InlineData("contoso.cloud.redislabs.com", MaintenanceNotificationMode.Auto)] // and Redis Cloud, which emits them
    [InlineData("contoso.redis.cache.windows.net", MaintenanceNotificationMode.Disabled)] // classic Azure does not
    [InlineData("contoso.example.com", MaintenanceNotificationMode.Disabled)] // and neither does anything else
    public void MaintenanceNotificationDefaultPerProvider(string hostName, MaintenanceNotificationMode expected)
    {
        // Auto rather than Enabled is what makes the pre-emptive default safe: AMR does not emit these yet,
        // so until the server side ships the opt-in is refused and the feature simply stays off
        var epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint(hostName, 0) });
        var provider = DefaultOptionsProvider.GetProvider(epc);
        Output.WriteLine($"{hostName} -> {provider.GetType().Name}");
        Assert.Equal(expected, provider.MaintenanceNotifications);

        // ...and it arrives through the options, not just off the provider
        var options = new ConfigurationOptions { EndPoints = { new DnsEndPoint(hostName, 0) } };
        Assert.Equal(expected, options.MaintenanceNotifications);
    }

    [Theory]
    [InlineData(RedisProtocol.Resp2)]
    [InlineData(RedisProtocol.Resp3)]
    public async Task AzureManagedRedisConnectsWithoutSubscriptionConnection(RedisProtocol protocol)
    {
        using var serverObj = new InProcessTestServer(Output, new DnsEndPoint("contoso.redis.azure.net", 10000), useSsl: true);
        var config = serverObj.GetClientConfig();
        config.ClientName = Guid.NewGuid().ToString().Replace("-", "");
        config.Protocol = protocol;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config, Writer);

        var server = conn.GetServer(conn.GetEndPoints().Single());
        var interactiveId = ((IInternalConnectionMultiplexer)conn).GetConnectionId(server.EndPoint, ConnectionType.Interactive);
        var clients = await server.ClientListAsync();
        var namedClients = clients.Where(x => x.Name == config.ClientName).ToArray();

        Assert.Equal(protocol, server.Protocol);
        Assert.NotNull(interactiveId);
        var self = Assert.Single(clients, x => x.Id == interactiveId);
        Assert.Equal(ClientType.Normal, self.ClientType);
        Assert.Equal(0, self.SubscriptionCount);
        Assert.Equal(0, self.PatternSubscriptionCount);
        Assert.Equal(0, self.ShardedSubscriptionCount);
        Assert.Equal(protocol, self.Protocol);

        var expectedCount = protocol is RedisProtocol.Resp3 ? 1 : 2;
        Assert.Equal(expectedCount, serverObj.ClientCount);
        Assert.Equal(expectedCount, namedClients.Length);

        await AssertCanPubSubAsync(conn, $"{nameof(AzureManagedRedisConnectsWithoutSubscriptionConnection)}:{protocol}");
    }

    [Fact]
    public async Task VanillaResp2ConnectsWithSeparatePubSubConnection()
    {
        using var serverObj = new InProcessTestServer(Output, new DnsEndPoint("redis.contoso.com", 10000), useSsl: true);
        var config = serverObj.GetClientConfig();
        config.Protocol = RedisProtocol.Resp2;
        Log($"QueueWhileDisconnected: {config.BacklogPolicy.QueueWhileDisconnected}");

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config, Writer);
        var sub = conn.GetSubscriber();
        await sub.SubscribeAsync(RedisChannel.Literal(nameof(VanillaResp2ConnectsWithSeparatePubSubConnection)), (_, _) => { });

        var server = conn.GetServer(conn.GetEndPoints().Single());
        var mux = (IInternalConnectionMultiplexer)conn;
        var interactiveId = mux.GetConnectionId(server.EndPoint, ConnectionType.Interactive);
        var subscriptionId = mux.GetConnectionId(server.EndPoint, ConnectionType.Subscription);
        var clients = server.ClientList();
        var namedClients = clients.Where(x => x.Name == conn.ClientName).ToArray();

        Assert.Equal(RedisProtocol.Resp2, server.Protocol);
        Assert.Equal(2, serverObj.ClientCount);
        Assert.NotNull(interactiveId);
        Assert.NotNull(subscriptionId);
        Assert.NotEqual(interactiveId, subscriptionId);
        Assert.Equal(2, namedClients.Length);

        var interactive = Assert.Single(clients, x => x.Id == interactiveId);
        var subscription = Assert.Single(clients, x => x.Id == subscriptionId);
        Assert.Equal(ClientType.Normal, interactive.ClientType);
        Assert.Equal(ClientType.PubSub, subscription.ClientType);
        Assert.True(subscription.SubscriptionCount > 0);

        await AssertCanPubSubAsync(conn, nameof(VanillaResp2ConnectsWithSeparatePubSubConnection));
    }

    private static async Task AssertCanPubSubAsync(ConnectionMultiplexer conn, string channelName)
    {
        var sub = conn.GetSubscriber();
        var channel = RedisChannel.Literal(channelName);
        var payload = (RedisValue)("payload:" + channelName);
        TaskCompletionSource<RedisValue> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await sub.SubscribeAsync(channel, (_, message) => tcs.TrySetResult(message));
        try
        {
            await sub.PublishAsync(channel, payload);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
            Assert.Same(tcs.Task, completed);
            Assert.Equal(payload, await tcs.Task);
        }
        finally
        {
            await sub.UnsubscribeAsync(channel);
        }
    }

    [Fact]
    public void AllOverridesFromDefaultsProp()
    {
        var options = ConfigurationOptions.Parse("localhost");
        Assert.IsType<DefaultOptionsProvider>(options.Defaults);
        options.Defaults = new TestOptionsProvider("");
        Assert.IsType<TestOptionsProvider>(options.Defaults);
        AssertAllOverrides(options);
    }

    [Fact]
    public void AllOverridesFromEndpointsParse()
    {
        DefaultOptionsProvider.AddProvider(new TestOptionsProvider(".parse"));
        var options = ConfigurationOptions.Parse("localhost.parse:6379");
        Assert.IsType<TestOptionsProvider>(options.Defaults);
        AssertAllOverrides(options);
    }

    private static void AssertAllOverrides(ConfigurationOptions options)
    {
        Assert.True(options.AbortOnConnectFail);
        Assert.Equal(TimeSpan.FromSeconds(123), TimeSpan.FromMilliseconds(options.ConnectTimeout));

        Assert.True(options.AllowAdmin);
        Assert.Equal(BacklogPolicy.FailFast, options.BacklogPolicy);
        Assert.True(options.CheckCertificateRevocation);

        Assert.True(options.CommandMap.IsAvailable(RedisCommand.SELECT));
        Assert.False(options.CommandMap.IsAvailable(RedisCommand.GET));

        Assert.Equal(TimeSpan.FromSeconds(124), TimeSpan.FromSeconds(options.ConfigCheckSeconds));
        Assert.Equal("TestConfigChannel", options.ConfigurationChannel);
        Assert.Equal(123, options.ConnectRetry);
        Assert.Equal(new Version(1, 2, 3, 4), options.DefaultVersion);

        Assert.True(options.HeartbeatConsistencyChecks);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.HeartbeatInterval);

        Assert.Equal(TimeSpan.FromSeconds(125), TimeSpan.FromSeconds(options.KeepAlive));
        Assert.Equal(NullLoggerFactory.Instance, options.LoggerFactory);
        Assert.Equal(Proxy.Twemproxy, options.Proxy);
        Assert.IsType<TestRetryPolicy>(options.ReconnectRetryPolicy);
        Assert.True(options.ResolveDns);
        Assert.Equal(TimeSpan.FromSeconds(126), TimeSpan.FromMilliseconds(options.SyncTimeout));
        Assert.Equal("TestTiebreaker", options.TieBreaker);
        Assert.Equal("TestUser", options.User);
        Assert.Equal("TestPassword", options.Password);
    }

    public class TestAfterConnectOptionsProvider : DefaultOptionsProvider
    {
        public int Calls;

        public override Task AfterConnectAsync(ConnectionMultiplexer muxer, Action<string> log)
        {
            Interlocked.Increment(ref Calls);
            log("TestAfterConnectOptionsProvider.AfterConnectAsync!");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AfterConnectAsyncHandler()
    {
        var options = ConfigurationOptions.Parse(GetConfiguration());
        var provider = new TestAfterConnectOptionsProvider();
        options.Defaults = provider;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);

        Assert.True(conn.IsConnected);
        Assert.Equal(1, provider.Calls);
    }

    public class TestClientNameOptionsProvider : DefaultOptionsProvider
    {
        protected override string GetDefaultClientName() => "Hey there";
    }

    [Fact]
    public async Task ClientNameOverride()
    {
        var options = ConfigurationOptions.Parse(GetConfiguration());
        options.Defaults = new TestClientNameOptionsProvider();

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);

        Assert.True(conn.IsConnected);
        Assert.Equal("Hey there", conn.ClientName);
    }

    [Fact]
    public async Task ClientNameExplicitWins()
    {
        var options = ConfigurationOptions.Parse(GetConfiguration() + ",name=FooBar");
        options.Defaults = new TestClientNameOptionsProvider();

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);

        Assert.True(conn.IsConnected);
        Assert.Equal("FooBar", conn.ClientName);
    }

    public class TestLibraryNameOptionsProvider : DefaultOptionsProvider
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public override string LibraryName => Id;
    }

    [Fact]
    public async Task LibraryNameOverride()
    {
        var options = ConfigurationOptions.Parse(GetConfiguration());
        var defaults = new TestLibraryNameOptionsProvider();
        options.AllowAdmin = true;
        options.Defaults = defaults;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
        // CLIENT SETINFO is in 7.2.0+
        TestBase.ThrowIfBelowMinVersion(conn, RedisFeatures.v7_2_0_rc1);

        var clients = await GetServer(conn).ClientListAsync();
        foreach (var client in clients)
        {
            Log("Library name: " + client.LibraryName);
        }

        Assert.True(conn.IsConnected);
        Assert.True(clients.Any(c => c.LibraryName == defaults.LibraryName), "Did not find client with name: " + defaults.Id);
    }
}
