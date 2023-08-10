using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;
using static StackExchange.Redis.Tests.SharedConnectionFixture;

namespace StackExchange.Redis.Tests;


public class ProtocolDependentFixture : IDisposable // without this, test perf is intolerable
{
    public const string Key = nameof(ProtocolDependentFixture);

    private NonDisposingConnection? resp2, resp3;
    internal IInternalConnectionMultiplexer GetConnection(TestBase obj, bool useResp3, [CallerMemberName] string caller = "")
    {
        Version? require = useResp3 ? RedisFeatures.v6_0_0 : null;
        lock (this)
        {
            if (useResp3)
            {
                return resp3 ??= new NonDisposingConnection(obj.Create(protocol: RedisProtocol.Resp3, require: require, caller: caller, shared: false, allowAdmin: true));
            }
            else
            {
                return resp2 ??= new NonDisposingConnection(obj.Create(protocol: RedisProtocol.Resp2, require: require, caller: caller, shared: false, allowAdmin: true));
            }
        }
    }

    public void Dispose()
    {
        resp2?.UnderlyingConnection?.Dispose();
        resp3?.UnderlyingConnection?.Dispose();
    }
}

[Collection(ProtocolDependentFixture.Key)]
public abstract class ProtocolDependentTestBase : TestBase // ProtocolDependentFixture has separate access to resp2/resp3 connections
{
    public ProtocolDependentTestBase(ITestOutputHelper output, ProtocolDependentFixture fixture) : base(output)
       => Fixture = fixture;

    public ProtocolDependentFixture Fixture { get; }
}


public abstract class ProtocolFixedTestBase : ProtocolDependentTestBase // extends that cability to apply/enforce correct RESP during Create
{
    public ProtocolFixedTestBase(ITestOutputHelper output, ProtocolDependentFixture fixture, bool resp3) : base(output, fixture)
       => Resp3 = resp3;

    public bool Resp3 { get; }

    internal new IInternalConnectionMultiplexer Create(string? clientName = null, int? syncTimeout = null, bool? allowAdmin = null, int? keepAlive = null, int? connectTimeout = null, string? password = null, string? tieBreaker = null, TextWriter? log = null, bool fail = true, string[]? disabledCommands = null, string[]? enabledCommands = null, bool checkConnect = true, string? failMessage = null, string? channelPrefix = null, Proxy? proxy = null, string? configuration = null, bool logTransactionData = true, bool shared = true, int? defaultDatabase = null, BacklogPolicy? backlogPolicy = null, Version? require = null, RedisProtocol? protocol = null, [CallerMemberName] string? caller = null)
    {
        if (protocol is not null)
        {
            Assert.True(Resp3 && protocol >= RedisProtocol.Resp3, "Test is requesting incorrect RESP");
        }
        if (shared && CanShare(allowAdmin, password, tieBreaker, fail, disabledCommands, enabledCommands, channelPrefix, proxy, configuration, defaultDatabase, backlogPolicy, protocol))
        {
            // can use the fixture's *pair* of resp clients
            var conn = Fixture.GetConnection(this, Resp3, caller!);
            ThrowIfBelowMinVersion(conn, require);
            return conn;
        }
        else
        {
            if (Resp3 && (require is null || require < RedisFeatures.v6_0_0))
            {
                require = RedisFeatures.v6_0_0;
            }
            return base.Create(clientName, syncTimeout, allowAdmin, keepAlive, connectTimeout, password, tieBreaker, log, fail, disabledCommands, enabledCommands, checkConnect, failMessage, channelPrefix, proxy, configuration, logTransactionData, shared,
                defaultDatabase, backlogPolicy, require, protocol, caller);
        }
    }


    //internal override IInternalConnectionMultiplexer Create(string? clientName = null, int? syncTimeout = null, bool? allowAdmin = null, int? keepAlive = null, int? connectTimeout = null, string? password = null, string? tieBreaker = null, TextWriter? log = null, bool fail = true, string[]? disabledCommands = null, string[]? enabledCommands = null, bool checkConnect = true, string? failMessage = null, string? channelPrefix = null, Proxy? proxy = null, string? configuration = null, bool logTransactionData = true, bool shared = true, int? defaultDatabase = null, BacklogPolicy? backlogPolicy = null, Version? require = null, RedisProtocol? protocol = null, [CallerMemberName] string? caller = null)
    //{
    //    var obj = base.Create(clientName, syncTimeout, allowAdmin, keepAlive, connectTimeout, password, tieBreaker, log, fail, disabledCommands, enabledCommands, checkConnect, failMessage, channelPrefix, proxy, configuration, logTransactionData, shared, defaultDatabase, backlogPolicy, require, protocol, caller);

    //    var ep = obj.GetEndPoints().FirstOrDefault();
    //    if (ep is not null)
    //    {
    //        var server = obj.GetServerEndPoint(ep);
    //        if (server is not null && server.IsResp3 != Resp3) Skip.Inconclusive("Incorrect RESP version");
    //    }
    //    return obj;
    //}
}

/// <summary>
/// See <see href="https://stackoverflow.com/questions/13829737/xunit-net-run-code-once-before-and-after-all-tests"/>.
/// </summary>
[CollectionDefinition(ProtocolDependentFixture.Key)]
public class ProtocolDependentCollection : ICollectionFixture<ProtocolDependentFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
