using StackExchange.Redis.Configuration;
using System;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class ConfigTests : TestBase
{
    public ConfigTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    public Version DefaultVersion = new (3, 0, 0);
    public Version DefaultAzureVersion = new (4, 0, 0);

    [Fact]
    public void SslProtocols_SingleValue()
    {
        var options = ConfigurationOptions.Parse("myhost,sslProtocols=Tls11");
        Assert.Equal(SslProtocols.Tls11, options.SslProtocols.GetValueOrDefault());
    }

    [Fact]
    public void SslProtocols_MultipleValues()
    {
        var options = ConfigurationOptions.Parse("myhost,sslProtocols=Tls11|Tls12");
        Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, options.SslProtocols.GetValueOrDefault());
    }

    [Theory]
    [InlineData("checkCertificateRevocation=false", false)]
    [InlineData("checkCertificateRevocation=true", true)]
    [InlineData("", true)]
    public void ConfigurationOption_CheckCertificateRevocation(string conString, bool expectedValue)
    {
        var options = ConfigurationOptions.Parse($"host,{conString}");
        Assert.Equal(expectedValue, options.CheckCertificateRevocation);
        var toString = options.ToString();
        Assert.Contains(conString, toString, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void SslProtocols_UsingIntegerValue()
    {
        // The below scenario is for cases where the *targeted*
        // .NET framework version (e.g. .NET 4.0) doesn't define an enum value (e.g. Tls11)
        // but the OS has been patched with support
        const int integerValue = (int)(SslProtocols.Tls11 | SslProtocols.Tls12);
        var options = ConfigurationOptions.Parse("myhost,sslProtocols=" + integerValue);
        Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, options.SslProtocols.GetValueOrDefault());
    }

    [Fact]
    public void SslProtocols_InvalidValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationOptions.Parse("myhost,sslProtocols=InvalidSslProtocol"));
    }

    [Fact]
    public void ConfigurationOptionsDefaultForAzure()
    {
        var options = ConfigurationOptions.Parse("contoso.redis.cache.windows.net");
        Assert.True(options.DefaultVersion.Equals(DefaultAzureVersion));
        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void ConfigurationOptionsForAzureWhenSpecified()
    {
        var options = ConfigurationOptions.Parse("contoso.redis.cache.windows.net,abortConnect=true, version=2.1.1");
        Assert.True(options.DefaultVersion.Equals(new Version(2, 1, 1)));
        Assert.True(options.AbortOnConnectFail);
    }

    [Fact]
    public void ConfigurationOptionsDefaultForAzureChina()
    {
        // added a few upper case chars to validate comparison
        var options = ConfigurationOptions.Parse("contoso.REDIS.CACHE.chinacloudapi.cn");
        Assert.True(options.DefaultVersion.Equals(DefaultAzureVersion));
        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void ConfigurationOptionsDefaultForAzureGermany()
    {
        var options = ConfigurationOptions.Parse("contoso.redis.cache.cloudapi.de");
        Assert.True(options.DefaultVersion.Equals(DefaultAzureVersion));
        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void ConfigurationOptionsDefaultForAzureUSGov()
    {
        var options = ConfigurationOptions.Parse("contoso.redis.cache.usgovcloudapi.net");
        Assert.True(options.DefaultVersion.Equals(DefaultAzureVersion));
        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void ConfigurationOptionsDefaultForNonAzure()
    {
        var options = ConfigurationOptions.Parse("redis.contoso.com");
        Assert.True(options.DefaultVersion.Equals(DefaultVersion));
        Assert.True(options.AbortOnConnectFail);
    }

    [Fact]
    public void ConfigurationOptionsDefaultWhenNoEndpointsSpecifiedYet()
    {
        var options = new ConfigurationOptions();
        Assert.True(options.DefaultVersion.Equals(DefaultVersion));
        Assert.True(options.AbortOnConnectFail);
    }

    [Fact]
    public void ConfigurationOptionsSyncTimeout()
    {
        // Default check
        var options = new ConfigurationOptions();
        Assert.Equal(5000, options.SyncTimeout);

        options = ConfigurationOptions.Parse("syncTimeout=20");
        Assert.Equal(20, options.SyncTimeout);
    }

    [Theory]
    [InlineData("127.1:6379", AddressFamily.InterNetwork, "127.0.0.1", 6379)]
    [InlineData("127.0.0.1:6379", AddressFamily.InterNetwork, "127.0.0.1", 6379)]
    [InlineData("2a01:9820:1:24::1:1:6379", AddressFamily.InterNetworkV6, "2a01:9820:1:24:0:1:1:6379", 0)]
    [InlineData("[2a01:9820:1:24::1:1]:6379", AddressFamily.InterNetworkV6, "2a01:9820:1:24::1:1", 6379)]
    public void ConfigurationOptionsIPv6Parsing(string configString, AddressFamily family, string address, int port)
    {
        var options = ConfigurationOptions.Parse(configString);
        Assert.Single(options.EndPoints);
        var ep = Assert.IsType<IPEndPoint>(options.EndPoints[0]);
        Assert.Equal(family, ep.AddressFamily);
        Assert.Equal(address, ep.Address.ToString());
        Assert.Equal(port, ep.Port);
    }

    [Fact]
    public void CanParseAndFormatUnixDomainSocket()
    {
        const string ConfigString = "!/some/path,allowAdmin=True";
#if NET472
        var ex = Assert.Throws<PlatformNotSupportedException>(() => ConfigurationOptions.Parse(ConfigString));
        Assert.Equal("Unix domain sockets require .NET Core 3 or above", ex.Message);
#else
        var config = ConfigurationOptions.Parse(ConfigString);
        Assert.True(config.AllowAdmin);
        var ep = Assert.IsType<UnixDomainSocketEndPoint>(Assert.Single(config.EndPoints));
        Assert.Equal("/some/path", ep.ToString());
        Assert.Equal(ConfigString, config.ToString());
#endif
    }

    [Fact]
    public void TalkToNonsenseServer()
    {
        var config = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            EndPoints =
            {
                { "127.0.0.1:1234" }
            },
            ConnectTimeout = 200
        };
        var log = new StringWriter();
        using (var conn = ConnectionMultiplexer.Connect(config, log))
        {
            Log(log.ToString());
            Assert.False(conn.IsConnected);
        }
    }

    [Fact]
    public async Task TestManualHeartbeat()
    {
        var options = ConfigurationOptions.Parse(GetConfiguration());
        options.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
        using var conn = await ConnectionMultiplexer.ConnectAsync(options);

        foreach (var ep in conn.GetServerSnapshot().ToArray())
        {
            ep.WriteEverySeconds = 1;
        }

        var db = conn.GetDatabase();
        db.Ping();

        var before = conn.OperationCount;

        Log("Sleeping to test heartbeat...");
        await UntilConditionAsync(TimeSpan.FromSeconds(5), () => conn.OperationCount > before + 1).ForAwait();
        var after = conn.OperationCount;

        Assert.True(after >= before + 1, $"after: {after}, before: {before}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(200)]
    public void GetSlowlog(int count)
    {
        using var conn = Create(allowAdmin: true);

        var rows = GetAnyPrimary(conn).SlowlogGet(count);
        Assert.NotNull(rows);
    }

    [Fact]
    public void ClearSlowlog()
    {
        using var conn = Create(allowAdmin: true);

        GetAnyPrimary(conn).SlowlogReset();
    }

    [Fact]
    public void ClientName()
    {
        using var conn = Create(clientName: "Test Rig", allowAdmin: true, shared: false);

        Assert.Equal("Test Rig", conn.ClientName);

        var db = conn.GetDatabase();
        db.Ping();

        var name = (string?)GetAnyPrimary(conn).Execute("CLIENT", "GETNAME");
        Assert.Equal("TestRig", name);
    }

    [Fact]
    public void DefaultClientName()
    {
        using var conn = Create(allowAdmin: true, caller: "", shared: false); // force default naming to kick in

        Assert.Equal($"{Environment.MachineName}(SE.Redis-v{Utils.GetLibVersion()})", conn.ClientName);
        var db = conn.GetDatabase();
        db.Ping();

        var name = (string?)GetAnyPrimary(conn).Execute("CLIENT", "GETNAME");
        Assert.Equal($"{Environment.MachineName}(SE.Redis-v{Utils.GetLibVersion()})", name);
    }

    [Fact]
    public void ReadConfigWithConfigDisabled()
    {
        using var conn = Create(allowAdmin: true, disabledCommands: new[] { "config", "info" });

        var server = GetAnyPrimary(conn);
        var ex = Assert.Throws<RedisCommandException>(() => server.ConfigGet());
        Assert.Equal("This operation has been disabled in the command-map and cannot be used: CONFIG", ex.Message);
    }

    [Fact]
    public void ConnectWithSubscribeDisabled()
    {
        using var conn = Create(allowAdmin: true, disabledCommands: new[] { "subscribe" });

        Assert.True(conn.IsConnected);
        var servers = conn.GetServerSnapshot();
        Assert.True(servers[0].IsConnected);
        if (!Context.IsResp3)
        {
            Assert.False(servers[0].IsSubscriberConnected);
        }

        var ex = Assert.Throws<RedisCommandException>(() => conn.GetSubscriber().Subscribe(RedisChannel.Literal(Me()), (_, _) => GC.KeepAlive(this)));
        Assert.Equal("This operation has been disabled in the command-map and cannot be used: SUBSCRIBE", ex.Message);
    }

    [Fact]
    public void ReadConfig()
    {
        using var conn = Create(allowAdmin: true);

        Log("about to get config");
        var server = GetAnyPrimary(conn);
        var all = server.ConfigGet();
        Assert.True(all.Length > 0, "any");

        var pairs = all.ToDictionary(x => (string)x.Key, x => (string)x.Value, StringComparer.InvariantCultureIgnoreCase);

        Assert.Equal(all.Length, pairs.Count);
        Assert.True(pairs.ContainsKey("timeout"), "timeout");
        var val = int.Parse(pairs["timeout"]);

        Assert.True(pairs.ContainsKey("port"), "port");
        val = int.Parse(pairs["port"]);
        Assert.Equal(TestConfig.Current.PrimaryPort, val);
    }

    [Fact]
    public void GetTime()
    {
        using var conn = Create();

        var server = GetAnyPrimary(conn);
        var serverTime = server.Time();
        var localTime = DateTime.UtcNow;
        Log("Server: " + serverTime.ToString(CultureInfo.InvariantCulture));
        Log("Local: " + localTime.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(localTime, serverTime, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void DebugObject()
    {
        using var conn = Create(allowAdmin: true);

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringIncrement(key, flags: CommandFlags.FireAndForget);
        var debug = (string?)db.DebugObject(key);
        Assert.NotNull(debug);
        Assert.Contains("encoding:int serializedlength:2", debug);
    }

    [Fact]
    public void GetInfo()
    {
        using var conn = Create(allowAdmin: true);

        var server = GetAnyPrimary(conn);
        var info1 = server.Info();
        Assert.True(info1.Length > 5);
        Log("All sections");
        foreach (var group in info1)
        {
            Log(group.Key);
        }
        var first = info1[0];
        Log("Full info for: " + first.Key);
        foreach (var setting in first)
        {
            Log("{0}  ==>  {1}", setting.Key, setting.Value);
        }

        var info2 = server.Info("cpu");
        Assert.Single(info2);
        var cpu = info2.Single();
        var cpuCount = cpu.Count();
        Assert.True(cpuCount > 2);
        Assert.Equal("CPU", cpu.Key);
        Assert.Contains(cpu, x => x.Key == "used_cpu_sys");
        Assert.Contains(cpu, x => x.Key == "used_cpu_user");
    }

    [Fact]
    public void GetInfoRaw()
    {
        using var conn = Create(allowAdmin: true);

        var server = GetAnyPrimary(conn);
        var info = server.InfoRaw();
        Assert.Contains("used_cpu_sys", info);
        Assert.Contains("used_cpu_user", info);
    }

    [Fact]
    public void GetClients()
    {
        var name = Guid.NewGuid().ToString();
        using var conn = Create(clientName: name, allowAdmin: true, shared: false);

        var server = GetAnyPrimary(conn);
        var clients = server.ClientList();
        Assert.True(clients.Length > 0, "no clients"); // ourselves!
        Assert.True(clients.Any(x => x.Name == name), "expected: " + name);

        if (server.Features.ClientId)
        {
            var id = conn.GetConnectionId(server.EndPoint, ConnectionType.Interactive);
            Assert.NotNull(id);
            Assert.True(clients.Any(x => x.Id == id), "expected: " + id);
            id = conn.GetConnectionId(server.EndPoint, ConnectionType.Subscription);
            Assert.NotNull(id);
            Assert.True(clients.Any(x => x.Id == id), "expected: " + id);

            var self = clients.First(x => x.Id == id);
            if (server.Version.Major >= 7)
            {
                Assert.Equal(Context.Test.Protocol, self.Protocol);
            }
            else
            {
                Assert.Null(self.Protocol);
            }
        }
    }

    [Fact]
    public void SlowLog()
    {
        using var conn = Create(allowAdmin: true);

        var server = GetAnyPrimary(conn);
        server.SlowlogGet();
        server.SlowlogReset();
    }

    [Fact]
    public async Task TestAutomaticHeartbeat()
    {
        RedisValue oldTimeout = RedisValue.Null;
        using var configConn = Create(allowAdmin: true);

        try
        {
            configConn.GetDatabase();
            var srv = GetAnyPrimary(configConn);
            oldTimeout = srv.ConfigGet("timeout")[0].Value;
            srv.ConfigSet("timeout", 5);

            using var innerConn = Create();
            var innerDb = innerConn.GetDatabase();
            innerDb.Ping(); // need to wait to pick up configuration etc

            var before = innerConn.OperationCount;

            Log("sleeping to test heartbeat...");
            await Task.Delay(8000).ForAwait();

            var after = innerConn.OperationCount;
            Assert.True(after >= before + 1, $"after: {after}, before: {before}");
        }
        finally
        {
            if (!oldTimeout.IsNull)
            {
                var srv = GetAnyPrimary(configConn);
                srv.ConfigSet("timeout", oldTimeout);
            }
        }
    }

    [Fact]
    public void EndpointIteratorIsReliableOverChanges()
    {
        var eps = new EndPointCollection
        {
            { IPAddress.Loopback, 7999 },
            { IPAddress.Loopback, 8000 },
        };

        using var iter = eps.GetEnumerator();
        Assert.True(iter.MoveNext());
        Assert.Equal(7999, ((IPEndPoint)iter.Current).Port);
        eps[1] = new IPEndPoint(IPAddress.Loopback, 8001); // boom
        Assert.True(iter.MoveNext());
        Assert.Equal(8001, ((IPEndPoint)iter.Current).Port);
        Assert.False(iter.MoveNext());
    }

    [Fact]
    public void ThreadPoolManagerIsDetected()
    {
        var config = new ConfigurationOptions
        {
            EndPoints = { { IPAddress.Loopback, 6379 } },
            SocketManager = SocketManager.ThreadPool
        };

        using var conn = ConnectionMultiplexer.Connect(config);

        Assert.Same(PipeScheduler.ThreadPool, conn.SocketManager?.Scheduler);
    }

    [Fact]
    public void DefaultThreadPoolManagerIsDetected()
    {
        var config = new ConfigurationOptions
        {
            EndPoints = { { IPAddress.Loopback, 6379 } },
        };

        using var conn = ConnectionMultiplexer.Connect(config);

        Assert.Same(SocketManager.Shared.Scheduler, conn.SocketManager?.Scheduler);
    }

    [Theory]
    [InlineData("myDNS:myPort,password=myPassword,connectRetry=3,connectTimeout=15000,syncTimeout=15000,defaultDatabase=0,abortConnect=false,ssl=true,sslProtocols=Tls12", SslProtocols.Tls12)]
    [InlineData("myDNS:myPort,password=myPassword,abortConnect=false,ssl=true,sslProtocols=Tls12", SslProtocols.Tls12)]
#pragma warning disable CS0618 // Type or member is obsolete
    [InlineData("myDNS:myPort,password=myPassword,abortConnect=false,ssl=true,sslProtocols=Ssl3", SslProtocols.Ssl3)]
#pragma warning restore CS0618
    [InlineData("myDNS:myPort,password=myPassword,abortConnect=false,ssl=true,sslProtocols=Tls12 ", SslProtocols.Tls12)]
    public void ParseTlsWithoutTrailingComma(string configString, SslProtocols expected)
    {
        var config = ConfigurationOptions.Parse(configString);
        Assert.Equal(expected, config.SslProtocols);
    }

    [Theory]
    [InlineData("foo,sslProtocols=NotAThing", "Keyword 'sslProtocols' requires an SslProtocol value (multiple values separated by '|'); the value 'NotAThing' is not recognised.", "sslProtocols")]
    [InlineData("foo,SyncTimeout=ten", "Keyword 'SyncTimeout' requires an integer value; the value 'ten' is not recognised.", "SyncTimeout")]
    [InlineData("foo,syncTimeout=-42", "Keyword 'syncTimeout' has a minimum value of '1'; the value '-42' is not permitted.", "syncTimeout")]
    [InlineData("foo,AllowAdmin=maybe", "Keyword 'AllowAdmin' requires a boolean value; the value 'maybe' is not recognised.", "AllowAdmin")]
    [InlineData("foo,Version=current", "Keyword 'Version' requires a version value; the value 'current' is not recognised.", "Version")]
    [InlineData("foo,proxy=epoxy", "Keyword 'proxy' requires a proxy value; the value 'epoxy' is not recognised.", "proxy")]
    public void ConfigStringErrorsGiveMeaningfulMessages(string configString, string expected, string paramName)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationOptions.Parse(configString));
        Assert.StartsWith(expected, ex.Message); // param name gets concatenated sometimes
        Assert.Equal(paramName, ex.ParamName); // param name gets concatenated sometimes
    }

    [Fact]
    public void ConfigStringInvalidOptionErrorGiveMeaningfulMessages()
    {
        var ex = Assert.Throws<ArgumentException>(() => ConfigurationOptions.Parse("foo,flibble=value"));
        Assert.StartsWith("Keyword 'flibble' is not supported.", ex.Message); // param name gets concatenated sometimes
        Assert.Equal("flibble", ex.ParamName);
    }

    [Fact]
    public void NullApply()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1,name=FooApply");
        Assert.Equal("FooApply", options.ClientName);

        // Doesn't go boom
        var result = options.Apply(null!);
        Assert.Equal("FooApply", options.ClientName);
        Assert.Equal(result, options);
    }

    [Fact]
    public void Apply()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1,name=FooApply");
        Assert.Equal("FooApply", options.ClientName);

        var randomName = Guid.NewGuid().ToString();
        var result = options.Apply(options => options.ClientName = randomName);

        Assert.Equal(randomName, options.ClientName);
        Assert.Equal(randomName, result.ClientName);
        Assert.Equal(result, options);
    }

    [Fact]
    public void BeforeSocketConnect()
    {
        var options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort);
        int count = 0;
        options.BeforeSocketConnect = (endpoint, connType, socket) =>
        {
            Interlocked.Increment(ref count);
            Log($"Endpoint: {endpoint}, ConnType: {connType}, Socket: {socket}");
            socket.DontFragment = true;
            socket.Ttl = (short)(connType == ConnectionType.Interactive ? 12 : 123);
        };
        using var conn = ConnectionMultiplexer.Connect(options);
        Assert.True(conn.IsConnected);
        Assert.Equal(2, count);

        var endpoint = conn.GetServerSnapshot()[0];
        var interactivePhysical = endpoint.GetBridge(ConnectionType.Interactive)?.TryConnect(null);
        var subscriptionPhysical = endpoint.GetBridge(ConnectionType.Subscription)?.TryConnect(null);
        Assert.NotNull(interactivePhysical);
        Assert.NotNull(subscriptionPhysical);

        var interactiveSocket = interactivePhysical.VolatileSocket;
        var subscriptionSocket = subscriptionPhysical.VolatileSocket;
        Assert.NotNull(interactiveSocket);
        Assert.NotNull(subscriptionSocket);

        Assert.Equal(12, interactiveSocket.Ttl);
        Assert.Equal(123, subscriptionSocket.Ttl);
        Assert.True(interactiveSocket.DontFragment);
        Assert.True(subscriptionSocket.DontFragment);
    }

    [Fact]
    public async Task MutableOptions()
    {
        var options = ConfigurationOptions.Parse(TestConfig.Current.PrimaryServerAndPort + ",name=Details");
        options.LoggerFactory = NullLoggerFactory.Instance;
        var originalConfigChannel = options.ConfigurationChannel = "originalConfig";
        var originalUser = options.User = "originalUser";
        var originalPassword = options.Password = "originalPassword";
        Assert.Equal("Details", options.ClientName);
        using var conn = await ConnectionMultiplexer.ConnectAsync(options);

        // Same instance
        Assert.Same(options, conn.RawConfig);
        // Copies
        Assert.NotSame(options.EndPoints, conn.EndPoints);

        // Same until forked - it's not cloned
        Assert.Same(options.CommandMap, conn.CommandMap);
        options.CommandMap = CommandMap.Envoyproxy;
        Assert.NotSame(options.CommandMap, conn.CommandMap);

#pragma warning disable CS0618 // Type or member is obsolete
        // Defaults true
        Assert.True(options.IncludeDetailInExceptions);
        Assert.True(conn.IncludeDetailInExceptions);
        options.IncludeDetailInExceptions = false;
        Assert.False(options.IncludeDetailInExceptions);
        Assert.False(conn.IncludeDetailInExceptions);

        // Defaults false
        Assert.False(options.IncludePerformanceCountersInExceptions);
        Assert.False(conn.IncludePerformanceCountersInExceptions);
        options.IncludePerformanceCountersInExceptions = true;
        Assert.True(options.IncludePerformanceCountersInExceptions);
        Assert.True(conn.IncludePerformanceCountersInExceptions);
#pragma warning restore CS0618

        var newName = Guid.NewGuid().ToString();
        options.ClientName = newName;
        Assert.Equal(newName, conn.ClientName);

        // TODO: This forks due to memoization of the byte[] for efficiency
        // If we could cheaply detect change it'd be good to let this change
        const string newConfigChannel = "newConfig";
        options.ConfigurationChannel = newConfigChannel;
        Assert.Equal(newConfigChannel, options.ConfigurationChannel);
        Assert.NotNull(conn.ConfigurationChangedChannel);
        Assert.Equal(Encoding.UTF8.GetString(conn.ConfigurationChangedChannel), originalConfigChannel);

        Assert.Equal(originalUser, conn.RawConfig.User);
        Assert.Equal(originalPassword, conn.RawConfig.Password);
        var newPass = options.Password = "newPassword";
        Assert.Equal(newPass, conn.RawConfig.Password);
        Assert.Equal(options.LoggerFactory, conn.RawConfig.LoggerFactory);
    }

    [Theory]
    [InlineData("http://somewhere:22", "http:somewhere:22")]
    [InlineData("http:somewhere:22", "http:somewhere:22")]
    public void HttpTunnelCanRoundtrip(string input, string expected)
    {
        var config = ConfigurationOptions.Parse($"127.0.0.1:6380,tunnel={input}");
        var ip = Assert.IsType<IPEndPoint>(Assert.Single(config.EndPoints));
        Assert.Equal(6380, ip.Port);
        Assert.Equal("127.0.0.1", ip.Address.ToString());

        Assert.NotNull(config.Tunnel);
        Assert.Equal(expected, config.Tunnel.ToString());

        var cs = config.ToString();
        Assert.Equal($"127.0.0.1:6380,tunnel={expected}", cs);
    }

    private class CustomTunnel : Tunnel { }

    [Fact]
    public void CustomTunnelCanRoundtripMinusTunnel()
    {
        // we don't expect to be able to parse custom tunnels, but we should still be able to round-trip
        // the rest of the config, which means ignoring them *in both directions* (unless first party)
        var options = ConfigurationOptions.Parse("127.0.0.1,Ssl=true");
        options.Tunnel = new CustomTunnel();
        var cs = options.ToString();
        Assert.Equal("127.0.0.1,ssl=True", cs);
        options = ConfigurationOptions.Parse(cs);
        Assert.Null(options.Tunnel);
    }

    [Theory]
    [InlineData("server:6379", true)]
    [InlineData("server:6379,setlib=True", true)]
    [InlineData("server:6379,setlib=False", false)]
    public void DefaultConfigOptionsForSetLib(string configurationString, bool setlib)
    {
        var options = ConfigurationOptions.Parse(configurationString);
        Assert.Equal(setlib, options.SetClientLibrary);
        Assert.Equal(configurationString, options.ToString());
        options = options.Clone();
        Assert.Equal(setlib, options.SetClientLibrary);
        Assert.Equal(configurationString, options.ToString());
    }
}
