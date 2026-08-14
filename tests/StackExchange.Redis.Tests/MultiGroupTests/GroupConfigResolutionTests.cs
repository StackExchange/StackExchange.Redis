using System;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

/// <summary>
/// Verifies how a live group resolves its configuration: that group defaults reach the members, that a
/// per-member override wins, and that none of this mutates the caller's <see cref="ConfigurationOptions"/>.
/// </summary>
public class GroupConfigResolutionTests(ITestOutputHelper log)
{
    [Fact]
    public async Task GroupExposesItsOptions()
    {
        using var server0 = new InProcessTestServer(log, endpoint: new DnsEndPoint("alpha", 6379));
        using var server1 = new InProcessTestServer(log, endpoint: new DnsEndPoint("beta", 6379));

        MultiGroupOptions options = new MultiGroupOptions.Builder
        {
            FailbackDelay = TimeSpan.FromMinutes(4),
            RetryPolicy = new RetryPolicy.Builder { MaxAttempts = 6 },
        };

        ConnectionGroupMember[] members = [new(server0.GetClientConfig()), new(server1.GetClientConfig())];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);

        Assert.Same(options, conn.Options);
        Assert.Equal(TimeSpan.FromMinutes(4), conn.Options.FailbackDelay);
    }

    [Fact]
    public async Task WithRetryUsesTheGroupPolicy()
    {
        using var server0 = new InProcessTestServer(log, endpoint: new DnsEndPoint("alpha", 6379));
        using var server1 = new InProcessTestServer(log, endpoint: new DnsEndPoint("beta", 6379));

        RetryPolicy groupPolicy = new RetryPolicy.Builder { MaxAttempts = 6, RetryDelay = TimeSpan.Zero };
        MultiGroupOptions options = new MultiGroupOptions.Builder { RetryPolicy = groupPolicy };

        ConnectionGroupMember[] members = [new(server0.GetClientConfig()), new(server1.GetClientConfig())];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);

        // the parameterless overload resolves the policy from the group it is attached to
        var retrying = Assert.IsType<RetryDatabase>(conn.GetDatabase().WithRetry());
        Assert.Same(groupPolicy, retrying.Policy);

        // ...and an explicit policy still wins
        RetryPolicy explicitPolicy = new RetryPolicy.Builder { MaxAttempts = 2 };
        var explicitlyRetrying = Assert.IsType<RetryDatabase>(conn.GetDatabase().WithRetry(explicitPolicy));
        Assert.Same(explicitPolicy, explicitlyRetrying.Policy);
    }

    [Fact]
    public async Task GroupCircuitBreakerReachesMembersWithoutMutatingCallerConfig()
    {
        using var server0 = new InProcessTestServer(log, endpoint: new DnsEndPoint("alpha", 6379));
        using var server1 = new InProcessTestServer(log, endpoint: new DnsEndPoint("beta", 6379));

        CircuitBreaker groupBreaker = new CircuitBreaker.Builder { FailureRateThreshold = 42 };
        CircuitBreaker memberBreaker = new CircuitBreaker.Builder { FailureRateThreshold = 13 };

        var config0 = server0.GetClientConfig();
        var config1 = server1.GetClientConfig();

        MultiGroupOptions options = new MultiGroupOptions.Builder { CircuitBreaker = groupBreaker };
        ConnectionGroupMember[] members = [new(config0), new(config1) { CircuitBreaker = memberBreaker }];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);

        // the group default reached the first member's connection, and the override reached the second
        Assert.Same(groupBreaker, AsMultiplexer(members[0]).EffectiveCircuitBreaker);
        Assert.Same(memberBreaker, AsMultiplexer(members[1]).EffectiveCircuitBreaker);

        // ...and neither was written back into the caller's configuration, which remains reusable
        Assert.Null(config0.CircuitBreaker);
        Assert.Null(config1.CircuitBreaker);

        static ConnectionMultiplexer AsMultiplexer(ConnectionGroupMember member) => member.Multiplexer;
    }

    [Fact]
    public async Task DisabledHealthCheckLeavesMemberSelectableOnConnectivityAlone()
    {
        using var server0 = new InProcessTestServer(log, endpoint: new DnsEndPoint("alpha", 6379));
        using var server1 = new InProcessTestServer(log, endpoint: new DnsEndPoint("beta", 6379));

        // HealthCheck.None performs no probes and reports Inconclusive, which is not Unhealthy - so a
        // connected member stays eligible, and the higher weight still wins
        MultiGroupOptions options = new MultiGroupOptions.Builder { HealthCheck = HealthCheck.None };
        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig(), "alpha") { Weight = 1 },
            new(server1.GetClientConfig(), "beta") { Weight = 9 },
        ];

        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
        await GroupWait.AssertConnectedAsync(conn);
        Assert.Equal("beta", conn.ActiveMember?.Name);
        Assert.All(members, member => Assert.False(member.IsUnhealthy));
    }
}
