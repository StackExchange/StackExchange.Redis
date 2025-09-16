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
    [InlineData("contoso.redisenterprise.cache.azure.net")]
    [InlineData("contoso.redis.azure.net")]
    [InlineData("contoso.redis.chinacloudapi.cn")]
    [InlineData("contoso.redis.usgovcloudapi.net")]
    public void IsMatchOnAzureDomain(string hostName)
    {
        var epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint(hostName, 0) });
        var provider = DefaultOptionsProvider.GetProvider(epc);
        Assert.IsType<AzureOptionsProvider>(provider);
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

    public class TestAfterDisconnectOptionsProvider : DefaultOptionsProvider
    {
        public int Calls;

        public override Task AfterDisconnectAsync(ConnectionMultiplexer muxer)
        {
            Interlocked.Increment(ref Calls);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AfterDisconnectAsyncHandler()
    {
        var options = ConfigurationOptions.Parse(GetConfiguration());
        var provider = new TestAfterDisconnectOptionsProvider();
        options.Defaults = provider;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(options, Writer);
        await conn.CloseAsync();

        Assert.False(conn.IsConnected);
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
