using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    /// <summary>
    /// Creates a new <see cref="IConnectionMultiplexer"/> instance that manages connections to multiple
    /// redundant configurations, based on their availability and relative <see cref="ConnectionGroupMember.Weight"/>.
    /// </summary>
    /// <param name="members">The initial configurations to connect to.</param>
    /// <param name="options">Additional options for configuring this group.</param>
    /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
#pragma warning disable RS0026
    [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
    public static Task<IConnectionGroup> ConnectGroupAsync(ConnectionGroupMember[] members, MultiGroupOptions? options = null, TextWriter? log = null)
#pragma warning restore RS0026
    {
        // create a defensive copy of the array; we don't want callers being able to radically swap things!
        members = (ConnectionGroupMember[])members.Clone();
        options ??= MultiGroupOptions.Default;
        options.Freeze();
        return MultiGroupMultiplexer.ConnectAsync(members, options, log);
    }

    /// <summary>
    /// Creates a new <see cref="IConnectionMultiplexer"/> instance that manages connections to multiple
    /// redundant configurations, based on their availability and relative <see cref="ConnectionGroupMember.Weight"/>.
    /// </summary>
    /// <param name="member0">An initial configuration to connect to.</param>
    /// <param name="member1">An additional initial configuration to connect to.</param>
    /// <param name="options">Additional options for configuring this group.</param>
    /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
    [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
#pragma warning disable RS0026
    public static Task<IConnectionGroup> ConnectGroupAsync(
        ConnectionGroupMember member0,
        ConnectionGroupMember member1,
        MultiGroupOptions? options = null,
        TextWriter? log = null)
#pragma warning restore RS0026
    {
        options ??= MultiGroupOptions.Default;
        options.Freeze();
        return MultiGroupMultiplexer.ConnectAsync([member0, member1], options, log);
    }
}

/// <summary>
/// A configured member of a <see cref="MultiGroupMultiplexer"/>.
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
#pragma warning disable RS0016, RS0026
public sealed partial class ConnectionGroupMember(ConfigurationOptions configuration, string name = "")
#pragma warning restore RS0016, RS0026
{
    /// <summary>
    /// Create a new <see cref="ConnectionGroupMember"/> from a configuration string.
    /// </summary>
#pragma warning disable RS0016, RS0026
    public ConnectionGroupMember(string configuration, string name = "") : this(
        ConfigurationOptions.Parse(configuration))
#pragma warning restore RS0016, RS0026
    {
    }

    internal ConfigurationOptions Configuration => configuration;

    private int _activated; // each member can only be activated once

    /// <inheritdoc/>
    public override string ToString() => Name;

    private ConnectionMultiplexer? _muxer;

    internal ConnectionMultiplexer Multiplexer => _muxer ?? ThrowNoMuxer();

    [DoesNotReturn]
    private static ConnectionMultiplexer ThrowNoMuxer() =>
        throw new InvalidOperationException("Member is not connected.");

    // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
    internal void SetMultiplexer(ConnectionMultiplexer muxer)
        => Interlocked.Exchange(ref _muxer, muxer ?? ThrowNoMuxer());

    internal ConnectionMultiplexer? ClearMultiplexer() => Interlocked.Exchange(ref _muxer, null);

    internal void Init(int index)
    {
        // add a name if not provided
        if (string.IsNullOrWhiteSpace(Name))
        {
            var ep = Configuration.EndPoints.FirstOrDefault();
            if (ep is null)
            {
                Name = index.ToString();
            }
            else
            {
                Name = Format.ToString(ep);
            }
        }

        // check not already attached
        if (Interlocked.CompareExchange(ref _activated, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                $"Member '{Name}' is already associated with a group, and cannot be reused.");
        }
    }

    /// <summary>
    /// Indicates whether this group is currently connected.
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// The name of this group member.
    /// </summary>
    public string Name { get; private set; } = name;

    /// <summary>
    /// The relative weight of this group member; higher is preferred.
    /// </summary>
    public double Weight
    {
        // avoid "tearing", since we can't rule out this being updated concurrently, and the runtime
        // only guarantees atomicity for 32-bit reads/writes
        get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _weight64));
        set => Interlocked.Exchange(ref _weight64, BitConverter.DoubleToInt64Bits(value));
    }

    private long _weight64 = BitConverter.DoubleToInt64Bits(1.0);

    /// <summary>
    /// The measured latency to this member.
    /// </summary>
    public TimeSpan Latency => _latencyTicks is uint.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks(_latencyTicks);

    private uint _latencyTicks = uint.MaxValue;

    internal void SetLatency(uint ticks) => _latencyTicks = ticks;

    internal static uint ToLatencyTicks(TimeSpan latency)
    {
        long ticks = latency.Ticks;
        if (ticks <= 0)
        {
            return 0;
        }
        return ticks > uint.MaxValue ? uint.MaxValue : (uint)ticks;
    }

    internal void SetLatency(TimeSpan latency) => SetLatency(ToLatencyTicks(latency));

    internal static ConnectionGroupMember? Select(ConnectionGroupMember? x, ConnectionGroupMember? y)
    {
        if (x is null) return y;
        if (y is null) return x;

        // always prefer a connected endpoint
        bool xc = x.IsConnected, yc = y.IsConnected;
        if (xc != yc) return xc ? x : y;

        // prefer higher weight
        double xw = x.Weight, yw = y.Weight;
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (xw != yw) return xw > yw ? x : y;

        // then by latency
        uint xl = x._latencyTicks, yl = y._latencyTicks;
        return xl <= yl ? x : y;
    }

    internal GroupConnectionChangedEventArgs.ChangeType UpdateState(HealthCheck.HealthCheckResult result)
    {
        bool isConnected;
        if (_muxer is { IsConnected: true } muxer)
        {
            isConnected = result is not HealthCheck.HealthCheckResult.Unhealthy;
            SetLatency(muxer.UpdateLatency());
        }
        else
        {
            isConnected = false;
        }

        var oldConnected = IsConnected;
        IsConnected = isConnected;

        return isConnected == oldConnected ? GroupConnectionChangedEventArgs.ChangeType.Unknown
            : isConnected ? GroupConnectionChangedEventArgs.ChangeType.Reconnected
            : GroupConnectionChangedEventArgs.ChangeType.Disconnected;
    }
}

internal sealed partial class MultiGroupMultiplexer : IConnectionGroup
{
    private ConnectionMultiplexer? _active;
    private ConnectionGroupMember[] _members;

    public override string ToString()
    {
        var muxer = _active;
        ConnectionGroupMember? member = null;
        if (muxer is not null)
        {
            foreach (var m in _members)
            {
                if (ReferenceEquals(muxer, m.Multiplexer))
                {
                    member = m;
                    break;
                }
            }
        }

        return member is null ? "No active connection" : $"Connected to {member.Name}";
    }

    public ReadOnlySpan<ConnectionGroupMember> GetMembers() => _members;

    internal ConnectionMultiplexer Active
    {
        get
        {
            return _active ?? Throw();

            [DoesNotReturn]
            static ConnectionMultiplexer Throw() =>
                throw new InvalidOperationException("All connections are unavailable.");
        }
    }

    internal ConnectionGroupMember ActiveMember
    {
        get
        {
            var active = _active;
            foreach (var member in _members)
            {
                if (ReferenceEquals(active, member.Multiplexer))
                {
                    return member;
                }
            }

            return Throw();

            [DoesNotReturn]
            static ConnectionGroupMember Throw() =>
                throw new InvalidOperationException("All connections are unavailable.");
        }
    }

    internal static async Task<IConnectionGroup> ConnectAsync(ConnectionGroupMember[] members, MultiGroupOptions options, TextWriter? log)
    {
        for (int i = 0; i < members.Length; i++)
        {
            members[i].Init(i);
        }

        var pending = new Task<ConnectionMultiplexer>[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            var config = members[i].Configuration;
            config.AbortOnConnectFail = false;
            config.HeartbeatConsistencyChecks = true;
            pending[i] = ConnectionMultiplexer.ConnectAsync(config, log);
        }

        for (int i = 0; i < pending.Length; i++)
        {
            var muxer = await pending[i].ConfigureAwait(false);
            members[i].SetMultiplexer(muxer);
        }

        // run initial healthcheck and begin
        var result = new MultiGroupMultiplexer(members, options);
        await TryHealthCheckAndSelectPreferredGroupAsync(result).ForAwait();
        result.StartPolling();
        return result;
    }

    private readonly MultiGroupOptions _options;
    private MultiGroupMultiplexer(ConnectionGroupMember[] members, MultiGroupOptions options)
    {
        _options = options;
        _members = members;
        _active = null;
    }

    internal static async Task<bool> TryHealthCheckAndSelectPreferredGroupAsync(object? target)
    {
        if (target is MultiGroupMultiplexer typed)
        {
            try
            {
                if (typed.IsDisposed) return false;
                await typed.RunHealthCheckAsync().ForAwait();
                typed.SelectPreferredGroup();
            }
            catch (Exception ex)
            {
                typed.OnInternalError(ex, origin: "update group");
            }
            return true; // even if we fault: try again
        }
        return false;
    }

    private void StartPolling()
    {
        // use a weak-ref to avoid the loop keeping the object alive
        _ = Task.Run(() => PollAsync(new(this)));

        static async Task PollAsync(WeakReference weakRef)
        {
            while (TryGetHealthCheck(weakRef.Target, out var interval))
            {
                await Task.Delay(interval).ForAwait();
                if (!await TryHealthCheckAndSelectPreferredGroupAsync(weakRef.Target).ForAwait()) break;
            }
        }

        static bool TryGetHealthCheck(object? target, out TimeSpan interval)
        {
            if (target is MultiGroupMultiplexer typed
                && typed._options.HealthCheck is { } healthCheck)
            {
                interval = healthCheck.Interval;
                return interval > TimeSpan.Zero & interval != TimeSpan.MaxValue;
            }

            interval = TimeSpan.Zero;
            return false;
        }
    }

    internal bool IsDisposed => _disposed;

    private Task<HealthCheck.HealthCheckResult>[]? _reusableHealthCheckBuffer;

    internal async Task RunHealthCheckAsync()
    {
        if (_disposed) return;
        var healthCheck = _options.HealthCheck;
        var members = _members;
        var pending = HealthCheck.GetReusablePending(ref _reusableHealthCheckBuffer, members.Length);
        for (int i = 0; i < members.Length; i++)
        {
            pending[i] = healthCheck.CheckHealthAsync(members[i].Multiplexer);
        }

        await Task.WhenAll(pending).TimeoutAfter(healthCheck.TotalTimeoutMillis()).ForAwait();
        for (int i = 0; i < pending.Length; i++)
        {
            HealthCheck.HealthCheckResult result;
            if (pending[i].IsCompletedSuccessfully)
            {
                result = await pending[i].ForAwait();
            }
            else
            {
                _ = pending[i].ObserveErrors();
                result = HealthCheck.HealthCheckResult.Unhealthy;
            }

            var delta = members[i].UpdateState(result);
            if (delta != GroupConnectionChangedEventArgs.ChangeType.Unknown)
            {
                OnConnectionChanged(delta, members[i]);
            }
        }

        HealthCheck.PutReusablePending(ref _reusableHealthCheckBuffer, ref pending);
    }

    internal void SelectPreferredGroup()
    {
        if (_disposed) return;
        var previousMuxer = _active;
        ConnectionGroupMember? preferredMember = null, previousMember = null;
        var members = _members;
        foreach (var member in members)
        {
            if (previousMember is null && ReferenceEquals(member.Multiplexer, previousMuxer))
            {
                previousMember = member;
            }

            if (member.IsConnected)
            {
                preferredMember = ConnectionGroupMember.Select(preferredMember, member);
            }
        }

        _active = preferredMember?.Multiplexer;

        if (preferredMember is not null && !ReferenceEquals(preferredMember, previousMember))
        {
            OnConnectionChanged(
                GroupConnectionChangedEventArgs.ChangeType.ActiveChanged,
                preferredMember,
                previousMember);
        }
    }

    private List<ConnectionMultiplexer> DropAll()
    {
        _active = null;
        var members = Interlocked.Exchange(ref _members, []);
        if (members.Length is 0) return [];
        var muxers = new List<ConnectionMultiplexer>(members.Length);
        foreach (var member in members)
        {
            var muxer = member.ClearMultiplexer();
            if (muxer is not null) muxers.Add(muxer);
        }

        return muxers;
    }

    private bool _disposed;
    public void Dispose()
    {
        _disposed = true;
        foreach (var muxer in DropAll())
        {
            muxer.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        foreach (var muxer in DropAll())
        {
            await muxer.DisposeAsync();
        }
    }

    public string ClientName => Active.ClientName;
    public string Configuration => Active.Configuration;
    public int TimeoutMilliseconds => Active.TimeoutMilliseconds;

    public long OperationCount
    {
        get
        {
            long count = 0;
            foreach (var member in _members)
            {
                count += member.Multiplexer.OperationCount;
            }

            return count;
        }
    }

    [Obsolete]
    public bool PreserveAsyncOrder
    {
        get => Active.PreserveAsyncOrder;
        set => Active.PreserveAsyncOrder = value;
    }

    public bool IsConnected => Active.IsConnected;
    public bool IsConnecting => Active.IsConnecting;

    [Obsolete]
    public bool IncludeDetailInExceptions
    {
        get => Active.IncludeDetailInExceptions;
        set => Active.IncludeDetailInExceptions = value;
    }

    public int StormLogThreshold
    {
        get => Active.StormLogThreshold;
        set => Active.StormLogThreshold = value;
    }

    private Func<ProfilingSession?>? _profilingSessionProvider;

    public void RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider)
    {
        _profilingSessionProvider = profilingSessionProvider;
        foreach (var member in _members)
        {
            member.Multiplexer.RegisterProfiler(profilingSessionProvider);
        }
    }

    public ServerCounters GetCounters() => Active.GetCounters();

    private EventHandler<RedisErrorEventArgs>? _errorMessage;

    public event EventHandler<RedisErrorEventArgs>? ErrorMessage
    {
        add
        {
            if (AddHandler(ref _errorMessage, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ErrorMessage += value;
                }
            }
        }
        remove
        {
            if (RemoveHandler(ref _errorMessage, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ErrorMessage -= value;
                }
            }
        }
    }

    /// <summary>
    /// Add a handler, and return true if this is the *first* handler, which means we should subscribe the dependents.
    /// </summary>
    private static bool AddHandler<T>(ref T? field, T? value) where T : Delegate
    {
        if (value is null) return false;
        while (true) // loop until we win (competition)
        {
            var oldValue = field;
            var newValue = oldValue is null ? value : (T)Delegate.Combine(oldValue, value);

            if (ReferenceEquals(Interlocked.CompareExchange(ref field, newValue, oldValue), oldValue))
            {
                return oldValue is null;
            }
        }
    }

    /// <summary>
    /// Remove a handler, and return true if this is the *last* handler, which means we should unsubscribe the dependents.
    /// </summary>
    private static bool RemoveHandler<T>(ref T? field, T? value) where T : Delegate
    {
        if (value is null) return false;
        while (true) // loop until we win (competition)
        {
            var oldValue = field;
            var newValue = oldValue is null ? null : (T?)Delegate.Remove(oldValue, value);

            if (ReferenceEquals(Interlocked.CompareExchange(ref field, newValue, oldValue), oldValue))
            {
                return newValue is null;
            }
        }
    }

    /// <summary>
    /// Subscribe a child multiplexer to all local event handlers that have subscribers.
    /// </summary>
    private void AddEventHandlers(ConnectionMultiplexer muxer)
    {
        muxer.ErrorMessage += _errorMessage;
        muxer.ConnectionFailed += _connectionFailed;
        muxer.InternalError += _internalError;
        muxer.ConnectionRestored += _connectionRestored;
        muxer.ConfigurationChanged += _configurationChanged;
        muxer.ConfigurationChangedBroadcast += _configurationChangedBroadcast;
        muxer.ServerMaintenanceEvent += _serverMaintenanceEvent;
        muxer.HashSlotMoved += _hashSlotMoved;
    }

    /// <summary>
    /// Unsubscribe a child multiplexer from all local event handlers.
    /// </summary>
    private void RemoveEventHandlers(ConnectionMultiplexer? muxer)
    {
        if (muxer is null) return;
        muxer.ErrorMessage -= _errorMessage;
        muxer.ConnectionFailed -= _connectionFailed;
        muxer.InternalError -= _internalError;
        muxer.ConnectionRestored -= _connectionRestored;
        muxer.ConfigurationChanged -= _configurationChanged;
        muxer.ConfigurationChangedBroadcast -= _configurationChangedBroadcast;
        muxer.ServerMaintenanceEvent -= _serverMaintenanceEvent;
        muxer.HashSlotMoved -= _hashSlotMoved;
    }

    private EventHandler<ConnectionFailedEventArgs>? _connectionFailed;

    public event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed
    {
        add
        {
            if (AddHandler(ref _connectionFailed, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ConnectionFailed += value;
                }
            }
        }
        remove
        {
            if (RemoveHandler(ref _connectionFailed, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ConnectionFailed -= value;
                }
            }
        }
    }

    private EventHandler<InternalErrorEventArgs>? _internalError;

    public event EventHandler<InternalErrorEventArgs>? InternalError
    {
        add
        {
            if (AddHandler(ref _internalError, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.InternalError += value;
                }
            }
        }
        remove
        {
            if (RemoveHandler(ref _internalError, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.InternalError -= value;
                }
            }
        }
    }

    private EventHandler<ConnectionFailedEventArgs>? _connectionRestored;

    public event EventHandler<ConnectionFailedEventArgs>? ConnectionRestored
    {
        add
        {
            if (AddHandler(ref _connectionRestored, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ConnectionRestored += value;
                }
            }
        }
        remove
        {
            if (RemoveHandler(ref _connectionRestored, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ConnectionRestored -= value;
                }
            }
        }
    }

    private EventHandler<EndPointEventArgs>? _configurationChanged;

    public event EventHandler<EndPointEventArgs>? ConfigurationChanged
    {
        add
        {
            if (AddHandler(ref _configurationChanged, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ConfigurationChanged += value;
                }
            }
        }
        remove
        {
            if (RemoveHandler(ref _configurationChanged, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ConfigurationChanged -= value;
                }
            }
        }
    }

    private EventHandler<EndPointEventArgs>? _configurationChangedBroadcast;

    public event EventHandler<EndPointEventArgs>? ConfigurationChangedBroadcast
    {
        add
        {
            if (AddHandler(ref _configurationChangedBroadcast, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ConfigurationChangedBroadcast += value;
                }
            }
        }
        remove
        {
            if (RemoveHandler(ref _configurationChangedBroadcast, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ConfigurationChangedBroadcast -= value;
                }
            }
        }
    }

    private EventHandler<ServerMaintenanceEvent>? _serverMaintenanceEvent;

    public event EventHandler<ServerMaintenanceEvent>? ServerMaintenanceEvent
    {
        add
        {
            if (AddHandler(ref _serverMaintenanceEvent, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ServerMaintenanceEvent += value;
                }
            }
        }
        remove
        {
            if (RemoveHandler(ref _serverMaintenanceEvent, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.ServerMaintenanceEvent -= value;
                }
            }
        }
    }

    public EndPoint[] GetEndPoints(bool configuredOnly = false) => Active.GetEndPoints(configuredOnly);

    public void Wait(Task task) => Active.Wait(task);

    public T Wait<T>(Task<T> task) => Active.Wait(task);

    public void WaitAll(params Task[] tasks) => Active.WaitAll(tasks);

    private EventHandler<HashSlotMovedEventArgs>? _hashSlotMoved;

    public event EventHandler<HashSlotMovedEventArgs>? HashSlotMoved
    {
        add
        {
            if (AddHandler(ref _hashSlotMoved, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.HashSlotMoved += value;
                }
            }
        }
        remove
        {
            if (RemoveHandler(ref _hashSlotMoved, value))
            {
                foreach (var member in _members)
                {
                    member.Multiplexer.HashSlotMoved -= value;
                }
            }
        }
    }

    public int HashSlot(RedisKey key) => Active.HashSlot(key);

    private ISubscriber? _defaultSubscriber;

    public ISubscriber GetSubscriber(object? asyncState = null)
    {
        if (asyncState is null) return _defaultSubscriber ??= new MultiGroupSubscriber(this, null);
        return new MultiGroupSubscriber(this, asyncState);
    }

    public IDatabase GetDatabase(int db = -1, object? asyncState = null)
    {
        if (asyncState is null & db >= -1 & db <= ConnectionMultiplexer.MaxCachedDatabaseInstance)
        {
            return _databases[db + 1] ??= new MultiGroupDatabase(this, db, null);
        }

        return new MultiGroupDatabase(this, db, asyncState);
    }

    private readonly IDatabase?[] _databases = new IDatabase?[ConnectionMultiplexer.MaxCachedDatabaseInstance + 2];

    public IServer GetServer(string host, int port, object? asyncState = null)
    {
        Exception ex;
        try
        {
            // try "active" first, and preserve the exception
            return Active.GetServer(host, port, asyncState);
        }
        catch (Exception e)
        {
            ex = e;
        }

        foreach (var member in _members)
        {
            try
            {
                return member.Multiplexer.GetServer(host, port, asyncState);
            }
            catch (Exception e) { Debug.WriteLine(e.Message); }
        }

        throw ex;
    }

    public IServer GetServer(string hostAndPort, object? asyncState = null)
    {
        Exception ex;
        try
        {
            // try "active" first, and preserve the exception
            return Active.GetServer(hostAndPort, asyncState);
        }
        catch (Exception e)
        {
            ex = e;
        }

        foreach (var member in _members)
        {
            try
            {
                return member.Multiplexer.GetServer(hostAndPort, asyncState);
            }
            catch (Exception e) { Debug.WriteLine(e.Message); }
        }

        throw ex;
    }

    public IServer GetServer(IPAddress host, int port)
    {
        Exception ex;
        try
        {
            // try "active" first, and preserve the exception
            return Active.GetServer(host, port);
        }
        catch (Exception e)
        {
            ex = e;
        }

        foreach (var member in _members)
        {
            try
            {
                return member.Multiplexer.GetServer(host, port);
            }
            catch (Exception e) { Debug.WriteLine(e.Message); }
        }

        throw ex;
    }

    public IServer GetServer(EndPoint endpoint, object? asyncState = null)
    {
        Exception ex;
        try
        {
            // try "active" first, and preserve the exception
            return Active.GetServer(endpoint, asyncState);
        }
        catch (Exception e)
        {
            ex = e;
        }

        foreach (var member in _members)
        {
            try
            {
                return member.Multiplexer.GetServer(endpoint, asyncState);
            }
            catch (Exception e) { Debug.WriteLine(e.Message); }
        }

        throw ex;
    }

    public IServer GetServer(RedisKey key, object? asyncState = null, CommandFlags flags = CommandFlags.None) =>
        Active.GetServer(key, asyncState, flags);

    public IServer[] GetServers() => Active.GetServers();

    public Task<bool> ConfigureAsync(TextWriter? log = null) => Active.ConfigureAsync(log);

    public bool Configure(TextWriter? log = null) => Active.Configure(log);

    public string GetStatus() => Active.GetStatus();

    public void GetStatus(TextWriter log) => Active.GetStatus(log);

    public void Close(bool allowCommandsToComplete = true)
    {
        _disposed = true;
        foreach (var member in DropAll())
        {
            member.Close(allowCommandsToComplete);
        }
    }

    public async Task CloseAsync(bool allowCommandsToComplete = true)
    {
        _disposed = true;
        foreach (var member in DropAll())
        {
            await member.CloseAsync(allowCommandsToComplete);
        }
    }

    public string? GetStormLog() => Active.GetStormLog();

    public void ResetStormLog() => Active.ResetStormLog();

    public long PublishReconfigure(CommandFlags flags = CommandFlags.None) => Active.PublishReconfigure(flags);

    public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) =>
        Active.PublishReconfigureAsync(flags);

    public int GetHashSlot(RedisKey key) => Active.GetHashSlot(key);

    public void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.All) =>
        Active.ExportConfiguration(destination, options);

    private readonly HashSet<string> _suffixes = new(); // in case we need to add to a new muxer
    public void AddLibraryNameSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix)) return; // trivial
        bool isNew;
        lock (_suffixes)
        {
            isNew = _suffixes.Add(suffix);
        }

        if (isNew)
        {
            foreach (var member in _members)
            {
                member.Multiplexer.AddLibraryNameSuffix(suffix);
            }
        }
    }

    public event EventHandler<GroupConnectionChangedEventArgs>? ConnectionChanged;

    private void OnConnectionChanged(
        GroupConnectionChangedEventArgs.ChangeType changeType,
        ConnectionGroupMember member,
        ConnectionGroupMember? previousMember = null)
    {
        var handler = ConnectionChanged;
        if (handler is not null)
        {
            new GroupConnectionChangedEventArgs(changeType, member, previousMember)
                .CompleteAsWorker(handler, this);
        }
    }

    public async Task AddAsync(ConnectionGroupMember member, TextWriter? log = null)
    {
        // connect
        member.Init(_members.Length);
        member.Configuration.HeartbeatConsistencyChecks = true;
        var muxer = await ConnectionMultiplexer.ConnectAsync(member.Configuration, log).ConfigureAwait(false);
        member.SetMultiplexer(muxer);
        var health = await _options.HealthCheck.CheckHealthAsync(muxer).ConfigureAwait(false);
        member.UpdateState(health);

        // apply any shared hooks
        AddEventHandlers(muxer);
        if (_profilingSessionProvider is not null) muxer.RegisterProfiler(_profilingSessionProvider);
        lock (_suffixes)
        {
            foreach (var suffix in _suffixes)
            {
                muxer.AddLibraryNameSuffix(suffix);
            }
        }

        // update the members array
        while (true)
        {
            var arr = _members;
            var newArr = new ConnectionGroupMember[arr.Length + 1];
            Array.Copy(arr, 0, newArr, 0, arr.Length);
            newArr[arr.Length] = member;
            if (Interlocked.CompareExchange(ref _members, newArr, arr) == arr) break;
        }

        OnConnectionChanged(GroupConnectionChangedEventArgs.ChangeType.Added, member);
        SelectPreferredGroup();

        // pub/sub
        await AddPubSubHandlersAsync(member).ConfigureAwait(false);
    }

    public bool Remove(ConnectionGroupMember group)
    {
        while (true)
        {
            var arr = _members;
            int index = -1;
            for (int i = 0; i < arr.Length; i++)
            {
                if (ReferenceEquals(arr[i], group))
                {
                    index = i;
                    break;
                }
            }

            if (index == -1) return false;
            var newArr = new ConnectionGroupMember[arr.Length - 1];
            if (index > 0) Array.Copy(arr, 0, newArr, 0, index);
            if (index < newArr.Length) Array.Copy(arr, index + 1, newArr, index, newArr.Length - index);
            if (Interlocked.CompareExchange(ref _members, newArr, arr) == arr) break;
        }

        var muxer = group.ClearMultiplexer();
        RemoveEventHandlers(muxer);
        OnConnectionChanged(GroupConnectionChangedEventArgs.ChangeType.Removed, group);
        SelectPreferredGroup();
        muxer?.Dispose();
        return true;
    }

    internal void OnHeartbeat() // for testing, to update latency etc
    {
        foreach (var member in _members)
        {
            member.Multiplexer.OnHeartbeat();
        }
    }

    internal void OnInternalError(Exception exception, EndPoint? endpoint = null, ConnectionType connectionType = ConnectionType.None, string? origin = null)
    {
        var handler = _internalError;
        if (handler is not null)
        {
            InternalErrorEventArgs args = new(handler, this, endpoint, connectionType, exception, origin);
            ConnectionMultiplexer.CompleteAsWorker(args);
        }
    }
}
