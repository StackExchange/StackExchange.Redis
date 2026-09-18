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
using StackExchange.Redis.Availability;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis
{
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
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
        public static Task<IConnectionGroup> ConnectGroupAsync(
            ConnectionGroupMember[] members,
            MultiGroupOptions? options = null,
            TextWriter? log = null)
#pragma warning restore RS0026
        {
            // create a defensive copy of the array; we don't want callers being able to radically swap things!
            members = (ConnectionGroupMember[])members.Clone();
            return MultiGroupMultiplexer.ConnectAsync(members, options ?? MultiGroupOptions.Default, log);
        }

        /// <summary>
        /// Creates a new <see cref="IConnectionMultiplexer"/> instance that manages connections to multiple
        /// redundant configurations, based on their availability and relative <see cref="ConnectionGroupMember.Weight"/>.
        /// </summary>
        /// <param name="member0">An initial configuration to connect to.</param>
        /// <param name="member1">An additional initial configuration to connect to.</param>
        /// <param name="options">Additional options for configuring this group.</param>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
#pragma warning disable RS0026
        public static Task<IConnectionGroup> ConnectGroupAsync(
            ConnectionGroupMember member0,
            ConnectionGroupMember member1,
            MultiGroupOptions? options = null,
            TextWriter? log = null)
#pragma warning restore RS0026
        {
            return MultiGroupMultiplexer.ConnectAsync([member0, member1], options ?? MultiGroupOptions.Default, log);
        }
    }

#pragma warning disable SA1403
    namespace Availability
#pragma warning restore SA1403
    {
        /// <summary>
        /// A configured member of a <see cref="MultiGroupMultiplexer"/>.
        /// </summary>
        [Experimental(Experiments.GeoRedundantFailover, UrlFormat = Experiments.UrlFormat)]
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

            // all of the simple boolean state for a member is packed into a single flags field, updated atomically
            [Flags]
            private enum MemberFlags
            {
                None = 0,
                Activated = 1 << 0,              // attached to a group; set exactly once (see Init)
                Connected = 1 << 1,              // last observed health state (see IsConnected)
                ExplicitOverrideFlag = 1 << 2,   // manual failover target (see ExplicitOverride)
                SkipInitialHealthCheck = 1 << 3, // see SkipInitialHealthCheck
                Unhealthy = 1 << 4,              // disabled by a health-check or circuit-breaker
            }

            private int _flags;

            private bool GetFlag(MemberFlags flag) => (Volatile.Read(ref _flags) & (int)flag) != 0;

            private void SetFlag(MemberFlags flag, bool value)
            {
                int set = value ? (int)flag : 0, clear = value ? 0 : (int)flag;
                while (true)
                {
                    int old = Volatile.Read(ref _flags);
                    int updated = (old & ~clear) | set;
                    if (updated == old || Interlocked.CompareExchange(ref _flags, updated, old) == old) return;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => Name;

            private ConnectionMultiplexer? _muxer;

            internal ConnectionMultiplexer Multiplexer => _muxer ?? ThrowNoMuxer();

            internal void SetUnhealthy()
            {
                // stored as raw UTC ticks and only ever compared as a long against the failback cutoff;
                // we never round-trip through a DateTime, so DateTimeKind never enters the picture
                Volatile.Write(ref _lastUnhealthyTicks, DateTime.UtcNow.Ticks);
                SetFlag(MemberFlags.Unhealthy, true);
            }

            // UTC ticks (DateTime.Ticks and TimeSpan.Ticks share the same 100ns unit); long for atomicity
            private long _lastUnhealthyTicks;

            /// <summary>
            /// Clear the <see cref="IsUnhealthy"/> flag against this endpoint, allowing it to be reconsidered.
            /// </summary>
            public void ResetIsUnhealthy() => SetFlag(MemberFlags.Unhealthy, false);

            /// <summary>
            /// Gets whether the endpoint failed a health-check or circuit-breaker test.
            /// </summary>
            public bool IsUnhealthy => GetFlag(MemberFlags.Unhealthy);

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

                // check not already attached (atomic test-and-set of the Activated flag)
                while (true)
                {
                    int old = Volatile.Read(ref _flags);
                    if ((old & (int)MemberFlags.Activated) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Member '{Name}' is already associated with a group, and cannot be reused.");
                    }

                    if (Interlocked.CompareExchange(ref _flags, old | (int)MemberFlags.Activated, old) == old) break;
                }
            }

            /// <summary>
            /// Indicates whether this group is currently connected.
            /// </summary>
            public bool IsConnected
            {
                get => GetFlag(MemberFlags.Connected);
                private set => SetFlag(MemberFlags.Connected, value);
            }

            /// <summary>
            /// Indicates whether the initial health-check should be skipped when this member is added to a group.
            /// When <c>true</c>, the member is added immediately - as not-yet-connected - and only becomes selectable
            /// once it subsequently passes a health-check, rather than blocking the add on an initial probe. This
            /// allows adding a member that is not yet healthy.
            /// </summary>
            public bool SkipInitialHealthCheck
            {
                get => GetFlag(MemberFlags.SkipInitialHealthCheck);
                set => SetFlag(MemberFlags.SkipInitialHealthCheck, value);
            }

            /// <summary>
            /// The name of this group member.
            /// </summary>
            public string Name { get; private set; } = name;

            // ---- per-member overrides of the group-wide MultiGroupOptions defaults ----
            // every value on MultiGroupOptions that can vary per member appears here as a nullable
            // counterpart; null means "use the group default". These are read when the member is added
            // to a group (for the circuit-breaker, which is fixed at connection construction) or on each
            // health-check pass (for the rest), so changing them later is only meaningful for the latter.

            /// <summary>
            /// The health-check to use for this member; when <see langword="null"/>,
            /// <see cref="MultiGroupOptions.HealthCheck"/> is used. Use <see cref="Availability.HealthCheck.None"/>
            /// to leave this member's selection driven purely by its observed connectivity.
            /// </summary>
            public HealthCheck? HealthCheck { get; set; }

            /// <summary>
            /// The circuit-breaker to use for this member; when <see langword="null"/>, the member's own
            /// <see cref="ConfigurationOptions.CircuitBreaker"/> is used, else
            /// <see cref="MultiGroupOptions.CircuitBreaker"/>. This is applied when the member connects, so
            /// setting it after the member has been added to a group has no effect on existing connections.
            /// </summary>
            public CircuitBreaker? CircuitBreaker { get; set; }

            /// <summary>
            /// How long this member must remain healthy, following its most recent failure, before it is
            /// eligible to be selected again; when <see langword="null"/>,
            /// <see cref="MultiGroupOptions.FailbackDelay"/> is used.
            /// </summary>
            public TimeSpan? FailbackDelay { get; set; }

            // the breaker to hand to this member's connections: member override, else its own config, else the group
            internal CircuitBreaker? ResolveCircuitBreaker(MultiGroupOptions options)
                => CircuitBreaker ?? Configuration.CircuitBreaker ?? options.CircuitBreaker;

            internal HealthCheck ResolveHealthCheck(MultiGroupOptions options) => HealthCheck ?? options.HealthCheck;

            internal TimeSpan ResolveFailbackDelay(MultiGroupOptions options) => FailbackDelay ?? options.FailbackDelay;

            /// <summary>
            /// The relative weight of this group member; higher is preferred.
            /// </summary>
            public double Weight
            {
                // avoid "tearing", since we can't rule out this being updated concurrently: Volatile.Read/Write
                // on a double are atomic even on 32-bit processors, so no read-modify-write dance is needed
                get => Volatile.Read(ref _weight);
                set => Volatile.Write(ref _weight, value);
            }

            internal bool ExplicitOverride
            {
                get => GetFlag(MemberFlags.ExplicitOverrideFlag);
                set => SetFlag(MemberFlags.ExplicitOverrideFlag, value);
            }

            private double _weight = 1.0;

            /// <summary>
            /// The measured latency to this member.
            /// </summary>
            public TimeSpan Latency =>
                _latencyTicks is uint.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks(_latencyTicks);

            internal bool ConsiderActive => // "IsConnected && !IsUnhealthy"
                ((MemberFlags)Volatile.Read(ref _flags) & (MemberFlags.Connected | MemberFlags.Unhealthy)) is MemberFlags.Connected;

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

            internal static ConnectionGroupMember? Select(ConnectionGroupMember? x, ConnectionGroupMember? y, ConnectionMultiplexer? active)
            {
                if (x is null) return y;
                if (y is null) return x;

                // always prefer a connected endpoint
                bool xc = x.IsConnected, yc = y.IsConnected;
                if (xc != yc) return xc ? x : y;

                // prefer manual override if only one is overridden
                xc = x.ExplicitOverride;
                yc = y.ExplicitOverride;
                if (xc != yc) return xc ? x : y;

                // prefer higher weight
                double xw = x.Weight, yw = y.Weight;
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (xw != yw) return xw > yw ? x : y;

                // then by latency
                uint xl = x._latencyTicks, yl = y._latencyTicks;
                if (xl != yl) return xl < yl ? x : y;

                // getting hard to choose; is either of them the existing active node? choose that to prevent flapping
                if (ReferenceEquals(x._muxer, active)) return x;
                if (ReferenceEquals(y._muxer, active)) return y;

                // I've got nothing; choose x arbitrarily
                return x;
            }

            internal GroupConnectionChangedEventArgs.ChangeType UpdateState(HealthCheckResult result, long failbackFailureCutoffTicks)
            {
                bool isConnected;
                if (_muxer is { IsConnected: true } muxer)
                {
                    isConnected = result is not HealthCheckResult.Unhealthy;
                    SetLatency(muxer.UpdateLatency());
                }
                else
                {
                    isConnected = false;
                }

                if (isConnected)
                {
                    if (Volatile.Read(ref _lastUnhealthyTicks) < failbackFailureCutoffTicks) ResetIsUnhealthy();
                }
                else
                {
                    SetUnhealthy();
                }

                var oldConnected = IsConnected;
                IsConnected = isConnected;

                return isConnected == oldConnected ? GroupConnectionChangedEventArgs.ChangeType.Unknown
                    : isConnected ? GroupConnectionChangedEventArgs.ChangeType.Reconnected
                    : GroupConnectionChangedEventArgs.ChangeType.Disconnected;
            }

            internal void UpdateLatency()
            {
                if (_muxer is { } muxer) SetLatency(muxer.UpdateLatency());
            }
        }

        internal sealed partial class MultiGroupMultiplexer : IConnectionGroup
        {
            private ActiveStub _activeStub = new(null);

            private void SetActive(ConnectionMultiplexer? active)
            {
                ActiveStub? newObj = null;
                while (true)
                {
                    var oldObj = Volatile.Read(ref _activeStub);

                    // is it already the same?
                    if (ReferenceEquals(oldObj.Active, active))
                    {
                        newObj?.Dispose(); // never actually released to the world
                        return; // nothing to do!
                    }

                    newObj ??= new(active);
                    if (ReferenceEquals(Interlocked.CompareExchange(ref _activeStub, newObj, oldObj), oldObj))
                    {
                        // successful swap; flag the old one as failed-over
                        oldObj.Cancel(false);
                        return;
                    }

                    // race? redo from start, but we can keep our newObj if we created one
                }
            }

            public CancellationToken GetNextFailover() => _activeStub.Token;

            private sealed class ActiveStub(ConnectionMultiplexer? active) : CancellationTokenSource
            {
                public ConnectionMultiplexer? Active => active;
            }

            private ConnectionGroupMember[] _members;

            public override string ToString()
            {
                var muxer = _activeStub.Active;
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
                    return _activeStub.Active ?? Throw();

                    [DoesNotReturn]
                    static ConnectionMultiplexer Throw() =>
                        throw new InvalidOperationException("All connections are unavailable.");
                }
            }

            // non-throwing twin of Active, for callers that have a trivial answer when the group is fully down
            internal ConnectionMultiplexer? TryGetActive() => _activeStub.Active;

            // a completed "no endpoint" result, shared by the database/subscriber facades when the group is fully down
            internal static readonly Task<EndPoint?> NoEndpoint = Task.FromResult<EndPoint?>(null);

            private ConnectionGroupMember? GetActiveMember() => GetMember(_activeStub.Active);

            private ConnectionGroupMember? GetMember(ConnectionMultiplexer? muxer)
            {
                if (muxer is not null)
                {
                    foreach (var member in _members)
                    {
                        if (ReferenceEquals(muxer, member.Multiplexer))
                        {
                            return member;
                        }
                    }
                }

                return null;
            }

            ConnectionGroupMember? IConnectionGroup.ActiveMember => GetActiveMember();

            internal ConnectionGroupMember ActiveMember
            {
                get
                {
                    return GetActiveMember() ?? Throw();

                    [DoesNotReturn]
                    static ConnectionGroupMember Throw() =>
                        throw new InvalidOperationException("All connections are unavailable.");
                }
            }

            internal static async Task<IConnectionGroup> ConnectAsync(
                ConnectionGroupMember[] members,
                MultiGroupOptions options,
                TextWriter? log)
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

                    // note the resolved circuit-breaker is passed *alongside* the configuration rather than
                    // written into it; see ConnectionMultiplexer.GroupCircuitBreaker
                    pending[i] = ConnectionMultiplexer.ConnectGroupMemberAsync(config, log, members[i].ResolveCircuitBreaker(options));
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

            public MultiGroupOptions Options => _options;

            private MultiGroupMultiplexer(ConnectionGroupMember[] members, MultiGroupOptions options)
            {
                _options = options;
                _members = members;
                SetActive(null);

                _connectionFailedWithCircuitBreaker = OnMemberConnectionFailed;
                // multiplexers should already be attached (ConnectAsync sets them before constructing us)
                foreach (var member in members)
                {
                    member.Multiplexer?.ConnectionFailed += _connectionFailedWithCircuitBreaker;
                }
            }

            internal static async Task<bool> TryHealthCheckAndSelectPreferredGroupAsync(object? target)
            {
                if (target is MultiGroupMultiplexer typed)
                {
                    if (typed.IsDisposed) return false;

                    // serialize health-check + select: the poll loop and the circuit-breaker fast-path
                    // can both land here, and we must not run two overlapping check/select passes (they
                    // would race _active and emit duplicate change events). If a pass is already running,
                    // skip - it will select on our behalf, and we converge on the next tick regardless.
                    if (Interlocked.CompareExchange(ref typed._healthCheckGate, 1, 0) is not 0)
                    {
                        return true; // still a live target; keep polling
                    }

                    try
                    {
                        await typed.RunHealthCheckAsync().ForAwait();
                        typed.SelectPreferredGroup();
                    }
                    catch (Exception ex)
                    {
                        typed.OnInternalError(ex, origin: "update group");
                    }
                    finally
                    {
                        Volatile.Write(ref typed._healthCheckGate, 0);
                    }

                    return true; // even if we fault: try again
                }

                return false;
            }

            private void StartPolling()
            {
                // use a weak-ref to avoid the loop keeping the object alive; capturing the token (rather than
                // the muxer) lets us break out of the delay promptly on dispose without resurrecting the target
                var cancellationToken = _pollCancellation.Token;
                _ = Task.Run(() => PollAsync(new(this), cancellationToken));

                static async Task PollAsync(WeakReference weakRef, CancellationToken cancellationToken)
                {
                    while (TryGetHealthCheck(weakRef.Target, out var interval))
                    {
                        try
                        {
                            await Task.Delay(interval, cancellationToken).ForAwait();
                        }
                        catch (OperationCanceledException)
                        {
                            break; // disposed; stop polling without waiting out the interval
                        }

                        if (!await TryHealthCheckAndSelectPreferredGroupAsync(weakRef.Target).ForAwait()) break;
                    }
                }

                static bool TryGetHealthCheck(object? target, out TimeSpan interval)
                {
                    if (target is MultiGroupMultiplexer typed)
                    {
                        // note the interval is a group-level concern (how often we re-evaluate the active
                        // member), not a property of any individual health-check
                        interval = typed._options.HealthCheckInterval;
                        return interval > TimeSpan.Zero & interval != TimeSpan.MaxValue;
                    }

                    interval = TimeSpan.Zero;
                    return false;
                }
            }

            internal bool IsDisposed => _disposed;

            private Task<HealthCheckResult>[]? _reusableHealthCheckBuffer;
            private int _healthCheckGate; // 0 = idle, 1 = a check/select pass is in flight (see TryHealthCheckAndSelectPreferredGroupAsync)

            internal async Task RunHealthCheckAsync()
            {
                if (_disposed) return;
                var members = _members;
                if (members.Length == 0) return; // nothing to check (and no budget to compute)

                var pending = HealthCheck.GetReusablePending(ref _reusableHealthCheckBuffer, members.Length);

                // members can use different health-checks (see ConnectionGroupMember.HealthCheck), so the
                // budget for the whole pass is the largest individual budget
                int totalTimeoutMillis = 0;
                for (int i = 0; i < members.Length; i++)
                {
                    var muxer = members[i].Multiplexer;
                    var healthCheck = members[i].ResolveHealthCheck(_options);
                    totalTimeoutMillis = Math.Max(totalTimeoutMillis, healthCheck.TotalTimeoutMillis());
                    pending[i] = healthCheck.CheckHealthAsync(muxer);
                }

                await Task.WhenAll(pending).TimeoutAfter(totalTimeoutMillis).ForAwait();
                for (int i = 0; i < pending.Length; i++)
                {
                    HealthCheckResult result;
                    if (pending[i].IsCompletedSuccessfully)
                    {
                        result = await pending[i].ForAwait();
                    }
                    else
                    {
                        _ = pending[i].ObserveErrors();
                        result = HealthCheckResult.Unhealthy;
                    }

                    var delta = members[i].UpdateState(result, GetFailbackFailureCutoff(members[i]));
                    if (delta != GroupConnectionChangedEventArgs.ChangeType.Unknown)
                    {
                        OnConnectionChanged(delta, members[i]);
                    }
                }

                HealthCheck.PutReusablePending(ref _reusableHealthCheckBuffer, ref pending);
            }

            private long GetFailbackFailureCutoff(ConnectionGroupMember member)
            {
                // the minimum last-observed unhealthy time (as UTC ticks) that we'll allow for reconnect;
                // for example, if the FailbackDelay is 2 minutes, and the time is 14:32:55, then the last
                // failure must have happened at 14:30:55 or earlier. Pure long tick math on the wall clock:
                // DateTime.Ticks and TimeSpan.Ticks are the same 100ns unit, so the subtraction is valid
                var delay = member.ResolveFailbackDelay(_options);
                if (delay == TimeSpan.MaxValue) return long.MinValue; // manual mode: never auto-reset

                return DateTime.UtcNow.Ticks - delay.Ticks;
            }

            internal void SelectPreferredGroup()
            {
                if (_disposed) return;
                var existingActive = _activeStub.Active;
                ConnectionGroupMember? preferredMember = null, previousMember = null;
                var members = _members;
                foreach (var member in members)
                {
                    if (previousMember is null && ReferenceEquals(member.Multiplexer, existingActive))
                    {
                        previousMember = member;
                    }

                    if (member.ConsiderActive)
                    {
                        member.UpdateLatency(); // this can change passively

                        // (note that when in doubt, we prefer the active muxer, to prevent flapping)
                        preferredMember = ConnectionGroupMember.Select(preferredMember, member, existingActive);
                    }
                }

                SetActive(preferredMember?.Multiplexer);

                if (preferredMember is not null && !ReferenceEquals(preferredMember, previousMember))
                {
                    OnConnectionChanged(
                        GroupConnectionChangedEventArgs.ChangeType.ActiveChanged,
                        preferredMember,
                        previousMember);
                }
            }

            private readonly CancellationTokenSource _pollCancellation = new();

            private List<ConnectionMultiplexer> DropAll()
            {
                _pollCancellation.Cancel(); // stop the polling loop promptly (idempotent)
                SetActive(null);
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

            // Unlike most members, these intentionally do *not* go via Active (which throws when no member is
            // available); callers routinely use IsConnected/IsConnecting as a pre-flight check and expect a
            // 'false' result - not an exception - when the entire group is down.
            public bool IsConnected => _activeStub.Active?.IsConnected ?? false;
            public bool IsConnecting => _activeStub.Active?.IsConnecting ?? false;

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

            private int _circuitBreakerDebounce = 0;
            private void OnMemberConnectionFailed(object? sender, ConnectionFailedEventArgs e)
            {
                // deliberately scoped to this one failure type; re-probe and re-select promptly rather
                // than waiting for the next poll tick, so traffic routes away from the dropped member
                if (e.FailureType is ConnectionFailureType.CircuitBreaker
                    && Interlocked.CompareExchange(ref _circuitBreakerDebounce, 1, 0) is 0)
                {
                    GetMember(sender as ConnectionMultiplexer)?.SetUnhealthy();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await TryHealthCheckAndSelectPreferredGroupAsync(this);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                        }
                        finally
                        {
                            Volatile.Write(ref _circuitBreakerDebounce, 0);
                        }
                    });
                }

                // invoke any custom logic from consumers subscribing to the public events
                _connectionFailedExternal?.Invoke(sender, e);
            }

            /// <summary>
            /// Subscribe a child multiplexer to all local event handlers that have subscribers.
            /// </summary>
            private void AddEventHandlers(ConnectionMultiplexer muxer)
            {
                muxer.ErrorMessage += _errorMessage;
                muxer.ConnectionFailed += _connectionFailedWithCircuitBreaker; // always non-null
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
                muxer.ConnectionFailed -= _connectionFailedWithCircuitBreaker; // always non-null
                muxer.InternalError -= _internalError;
                muxer.ConnectionRestored -= _connectionRestored;
                muxer.ConfigurationChanged -= _configurationChanged;
                muxer.ConfigurationChangedBroadcast -= _configurationChangedBroadcast;
                muxer.ServerMaintenanceEvent -= _serverMaintenanceEvent;
                muxer.HashSlotMoved -= _hashSlotMoved;
            }

            private readonly EventHandler<ConnectionFailedEventArgs> _connectionFailedWithCircuitBreaker;

            private EventHandler<ConnectionFailedEventArgs>? _connectionFailedExternal; // from public callers

            public event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed
            {
                // note we *do not* need to hook/unhook the inner connection - we always subscribe to that
                add => AddHandler(ref _connectionFailedExternal, value);
                remove => RemoveHandler(ref _connectionFailedExternal, value);
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

            // forwarding is not using: these decorators must implement the interface in full, and the
            // implementation cannot be dropped while the interface declares it
            #pragma warning disable SER308 // Blocking on a task through the library's Wait helpers
            public void Wait(Task task) => Active.Wait(task);

            public T Wait<T>(Task<T> task) => Active.Wait(task);

            public void WaitAll(params Task[] tasks) => Active.WaitAll(tasks);
            #pragma warning restore SER308

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

            private readonly IDatabase?[] _databases =
                new IDatabase?[ConnectionMultiplexer.MaxCachedDatabaseInstance + 2];

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
                member.Configuration.AbortOnConnectFail = false; // members are gated by health-checks, not connect-fail
                member.Configuration.HeartbeatConsistencyChecks = true;
                var muxer = await ConnectionMultiplexer.ConnectGroupMemberAsync(
                    member.Configuration, log, member.ResolveCircuitBreaker(_options)).ConfigureAwait(false);
                member.SetMultiplexer(muxer);

                // unless told otherwise, run an initial health-check so a healthy member can be selected immediately;
                // when skipped, the member is added as not-yet-connected and the poll loop brings it online once it
                // passes - this is the only way to add a member that is not yet healthy
                if (!member.SkipInitialHealthCheck)
                {
                    var health = await member.ResolveHealthCheck(_options).CheckHealthAsync(muxer).ConfigureAwait(false);
                    member.UpdateState(health, GetFailbackFailureCutoff(member));
                }

                // apply any shared hooks
                AddEventHandlers(muxer); // includes circuit-breaker
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

            public bool TryFailoverTo(ConnectionGroupMember? member)
            {
                if (member is null)
                {
                    // remove any explicit overrides, returning whether that was an actual change
                    bool result = false;
                    foreach (var m in _members)
                    {
                        if (m.ExplicitOverride)
                        {
                            result = true; // someone was explicitly enabled
                            m.ExplicitOverride = false;
                        }
                    }

                    SelectPreferredGroup();
                    return result;
                }

                var members = _members;
                if (!members.Contains(member))
                {
                    // not one of ours?
                    return false;
                }

                member.ResetIsUnhealthy(); // the user explicitly asked us to consider this node
                if (!member.IsConnected)
                {
                    // not allowed
                    return false;
                }

                if (member.ExplicitOverride)
                {
                    // already preferred; no change, but report as success
                    return true;
                }

                // otherwise, deselect everyone else, and select this one
                foreach (var m in members)
                {
                    m.ExplicitOverride = ReferenceEquals(m, member);
                }

                SelectPreferredGroup();
                return true;
            }

            public bool Remove(ConnectionGroupMember member)
            {
                while (true)
                {
                    var arr = _members;
                    int index = -1;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (ReferenceEquals(arr[i], member))
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

                var muxer = member.ClearMultiplexer();
                RemoveEventHandlers(muxer); // includes circuit-breaker
                OnConnectionChanged(GroupConnectionChangedEventArgs.ChangeType.Removed, member);
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

            internal void OnInternalError(
                Exception exception,
                EndPoint? endpoint = null,
                ConnectionType connectionType = ConnectionType.None,
                string? origin = null)
            {
                var handler = _internalError;
                if (handler is not null)
                {
                    InternalErrorEventArgs args = new(handler, this, endpoint, connectionType, exception, origin);
                    ConnectionMultiplexer.CompleteAsWorker(args);
                }
            }
        }
    }
}
