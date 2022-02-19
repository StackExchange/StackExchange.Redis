using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class DefaultOptions : TestBase
    {
        public DefaultOptions(ITestOutputHelper output) : base(output) { }

        public class TestOptionsProvider : DefaultOptionsProvider
        {
            private readonly string _domainSuffix;
            public TestOptionsProvider(string domainSuffix) => _domainSuffix = domainSuffix;

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
            public override bool IsMatch(EndPoint endpoint) => endpoint is DnsEndPoint dnsep && dnsep.Host.EndsWith(_domainSuffix);
            public override TimeSpan KeepAliveInterval => TimeSpan.FromSeconds(125);
            public override Proxy Proxy => Proxy.Twemproxy;
            public override IReconnectRetryPolicy ReconnectRetryPolicy => new TestRetryPolicy();
            public override bool ResolveDns => true;
            public override TimeSpan SyncTimeout => TimeSpan.FromSeconds(126);
            public override string TieBreaker => "TestTiebreaker";
        }

        public class TestRetryPolicy : IReconnectRetryPolicy
        {
            public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry) => false;
        }

        // TODO Testing
        // DefaultClientName

        [Fact]
        public void IsMatchOnDomain()
        {
            DefaultOptionsProvider.KnownProviders.Insert(0, new TestOptionsProvider(".testdomain"));

            var epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint("local.testdomain", 0) });
            var provider = DefaultOptionsProvider.GetForEndpoints(epc);
            Assert.IsType<TestOptionsProvider>(provider);

            epc = new EndPointCollection(new List<EndPoint>() { new DnsEndPoint("local.nottestdomain", 0) });
            provider = DefaultOptionsProvider.GetForEndpoints(epc);
            Assert.IsType<DefaultOptionsProvider>(provider);
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
            DefaultOptionsProvider.KnownProviders.Insert(0, new TestOptionsProvider(".parse"));
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

            Assert.Equal(TimeSpan.FromSeconds(125), TimeSpan.FromSeconds(options.KeepAlive));
            Assert.Equal(Proxy.Twemproxy, options.Proxy);
            Assert.IsType<TestRetryPolicy>(options.ReconnectRetryPolicy);
            Assert.True(options.ResolveDns);
            Assert.Equal(TimeSpan.FromSeconds(126), TimeSpan.FromMilliseconds(options.SyncTimeout));
            Assert.Equal("TestTiebreaker", options.TieBreaker);
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

            using var muxer = await ConnectionMultiplexer.ConnectAsync(options, Writer);

            Assert.True(muxer.IsConnected);
            Assert.Equal(1, provider.Calls);
        }
    }
}
