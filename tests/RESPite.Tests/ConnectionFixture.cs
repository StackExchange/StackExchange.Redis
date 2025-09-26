using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Testing.Platform.Extensions.Messages;
using RESPite.Connections;
using RESPite.StackExchange.Redis;
using StackExchange.Redis;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;
using Xunit;

[assembly: AssemblyFixture(typeof(RESPite.Tests.ConnectionFixture))]

namespace RESPite.Tests;

public class ConnectionFixture : IDisposable
{
    private readonly IConnectionMultiplexer _muxer;
    private readonly RespConnectionPool _pool = new();

    public ConnectionFixture()
    {
        _muxer = new DummyMultiplexer(this);
    }

    public void Dispose() => _pool.Dispose();

    public RespConnection GetConnection()
    {
        var template = _pool.Template.WithCancellationToken(TestContext.Current.CancellationToken);
        return _pool.GetConnection(template);
    }

    public IConnectionMultiplexer Multiplexer => _muxer;
}

internal sealed class DummyMultiplexer(ConnectionFixture fixture) : IConnectionMultiplexer
{
    public override string ToString() => nameof(DummyMultiplexer);
    private readonly ConnectionFixture _fixture = fixture;
    private readonly string clientName = "";
    private readonly string configuration = "";
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
    private int timeoutMilliseconds;
    private long operationCount;
    private bool preserveAsyncOrder;
    private bool isConnected;
    private bool isConnecting;
    private bool includeDetailInExceptions;
    private int stormLogThreshold;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

    void IDisposable.Dispose() { }

    ValueTask IAsyncDisposable.DisposeAsync() => default;

    string IConnectionMultiplexer.ClientName => clientName;

    string IConnectionMultiplexer.Configuration => configuration;

    int IConnectionMultiplexer.TimeoutMilliseconds => timeoutMilliseconds;

    long IConnectionMultiplexer.OperationCount => operationCount;

    bool IConnectionMultiplexer.PreserveAsyncOrder
    {
        get => preserveAsyncOrder;
        set => preserveAsyncOrder = value;
    }

    bool IConnectionMultiplexer.IsConnected => isConnected;

    bool IConnectionMultiplexer.IsConnecting => isConnecting;

    bool IConnectionMultiplexer.IncludeDetailInExceptions
    {
        get => includeDetailInExceptions;
        set => includeDetailInExceptions = value;
    }

    int IConnectionMultiplexer.StormLogThreshold
    {
        get => stormLogThreshold;
        set => stormLogThreshold = value;
    }

    void IConnectionMultiplexer.RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) =>
        throw new NotImplementedException();

    ServerCounters IConnectionMultiplexer.GetCounters() => throw new NotImplementedException();

    event EventHandler<RedisErrorEventArgs>? IConnectionMultiplexer.ErrorMessage
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionFailed
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    event EventHandler<InternalErrorEventArgs>? IConnectionMultiplexer.InternalError
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    event EventHandler<ConnectionFailedEventArgs>? IConnectionMultiplexer.ConnectionRestored
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChanged
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    event EventHandler<EndPointEventArgs>? IConnectionMultiplexer.ConfigurationChangedBroadcast
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    event EventHandler<ServerMaintenanceEvent>? IConnectionMultiplexer.ServerMaintenanceEvent
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    EndPoint[] IConnectionMultiplexer.GetEndPoints(bool configuredOnly) => throw new NotImplementedException();

    void IConnectionMultiplexer.Wait(Task task) => throw new NotImplementedException();

    T IConnectionMultiplexer.Wait<T>(Task<T> task) => throw new NotImplementedException();

    void IConnectionMultiplexer.WaitAll(params Task[] tasks) => throw new NotImplementedException();

    event EventHandler<HashSlotMovedEventArgs>? IConnectionMultiplexer.HashSlotMoved
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    int IConnectionMultiplexer.HashSlot(RedisKey key) => throw new NotImplementedException();

    ISubscriber IConnectionMultiplexer.GetSubscriber(object? asyncState) => throw new NotImplementedException();

    IDatabase IConnectionMultiplexer.GetDatabase(int db, object? asyncState) => throw new NotImplementedException();

    IServer IConnectionMultiplexer.GetServer(string host, int port, object? asyncState) =>
        throw new NotImplementedException();

    IServer IConnectionMultiplexer.GetServer(string hostAndPort, object? asyncState) =>
        throw new NotImplementedException();

    IServer IConnectionMultiplexer.GetServer(IPAddress host, int port) => throw new NotImplementedException();

    IServer IConnectionMultiplexer.GetServer(EndPoint endpoint, object? asyncState) =>
        throw new NotImplementedException();

    public IServer GetServer(RedisKey key, object? asyncState = null, CommandFlags flags = CommandFlags.None)
        => throw new NotImplementedException();

    IServer[] IConnectionMultiplexer.GetServers() => throw new NotImplementedException();

    Task<bool> IConnectionMultiplexer.ConfigureAsync(TextWriter? log) => throw new NotImplementedException();

    bool IConnectionMultiplexer.Configure(TextWriter? log) => throw new NotImplementedException();

    string IConnectionMultiplexer.GetStatus() => throw new NotImplementedException();

    void IConnectionMultiplexer.GetStatus(TextWriter log) => throw new NotImplementedException();

    void IConnectionMultiplexer.Close(bool allowCommandsToComplete) => throw new NotImplementedException();

    Task IConnectionMultiplexer.CloseAsync(bool allowCommandsToComplete) => throw new NotImplementedException();

    string? IConnectionMultiplexer.GetStormLog() => throw new NotImplementedException();

    void IConnectionMultiplexer.ResetStormLog() => throw new NotImplementedException();

    long IConnectionMultiplexer.PublishReconfigure(CommandFlags flags) => throw new NotImplementedException();

    Task<long> IConnectionMultiplexer.PublishReconfigureAsync(CommandFlags flags) =>
        throw new NotImplementedException();

    int IConnectionMultiplexer.GetHashSlot(RedisKey key) => throw new NotImplementedException();

    void IConnectionMultiplexer.ExportConfiguration(Stream destination, ExportOptions options) =>
        throw new NotImplementedException();

    void IConnectionMultiplexer.AddLibraryNameSuffix(string suffix) => throw new NotImplementedException();
}
