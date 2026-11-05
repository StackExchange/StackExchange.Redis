using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis
{
    internal sealed class PhysicalBridge : IDisposable
    {
        internal readonly string Name;

        private const int ProfileLogSamples = 10;

        private const double ProfileLogSeconds = (1000 /* ms */ * ProfileLogSamples) / 1000.0;

        private static readonly Message ReusableAskingCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.ASKING);

        private readonly long[] profileLog = new long[ProfileLogSamples];

        /// <summary>
        /// We have 1 queue in play on this bridge.
        /// We're bypassing the queue for handshake events that go straight to the socket.
        /// Everything else that's not an internal call goes into the queue if there is a queue.
        /// </summary>
        /// <remarks>
        /// In a later release we want to remove per-server events from this queue completely and shunt queued messages
        /// to another capable primary connection if one is available to process them faster (order is already hosed).
        /// For now, simplicity in: queue it all, replay or timeout it all.
        /// </remarks>
        private readonly ConcurrentQueue<Message> _backlog = new();

        private bool BacklogHasItems => !_backlog.IsEmpty;
        private int _backlogProcessorIsRunning = 0;
        private int _backlogCurrentEnqueued = 0;
        private long _backlogTotalEnqueued = 0;

        private int activeWriters = 0;
        private int beating;
        private int failConnectCount = 0;
        private volatile bool isDisposed;
        private volatile bool shouldResetConnectionRetryCount;
        private bool _needsReconnect;
        private long nonPreferredEndpointCount;

        // private volatile int missedHeartbeats;
        private long operationCount, socketCount;
        private volatile PhysicalConnection? physical;

        /// <summary>The current connection, if any; for diagnostics and tests.</summary>
        internal PhysicalConnection? Physical => physical;

        private long profileLastLog;
        private int profileLogIndex;

        private volatile bool reportNextFailure = true, reconfigureNextFailure = false;

        private volatile int state = (int)State.Disconnected;

        internal long? ConnectionId => physical?.ConnectionId;

        private readonly AwaitableMutex _singleWriter;

        internal string? PhysicalName => physical?.ToString();

        private uint _nextHighIntegrityToken; // zero means not enabled

        public DateTime? ConnectedAt { get; private set; }

        public PhysicalBridge(ServerEndPoint serverEndPoint, ConnectionType type, int timeoutMilliseconds)
        {
            ServerEndPoint = serverEndPoint;
            ConnectionType = type;
            Multiplexer = serverEndPoint.Multiplexer;
            Name = Format.ToString(serverEndPoint.EndPoint) + "/" + ConnectionType.ToString();
            _singleWriter = AwaitableMutex.Create(timeoutMilliseconds: timeoutMilliseconds);
            if (type == ConnectionType.Interactive && Multiplexer.RawConfig.HighIntegrity)
            {
                // we just need this to be non-zero to enable tracking
                _nextHighIntegrityToken = 1;
            }
        }

        public enum State : byte
        {
            Connecting,
            ConnectedEstablishing,
            ConnectedEstablished,
            Disconnected,
        }

        public Exception? LastException { get; private set; }

        public ConnectionType ConnectionType { get; }

        public bool IsConnected => state == (int)State.ConnectedEstablished;

        public bool IsConnecting => state == (int)State.ConnectedEstablishing || state == (int)State.Connecting;

        public ConnectionMultiplexer Multiplexer { get; }

        public ServerEndPoint ServerEndPoint { get; }

        public long SubscriptionCount => physical?.SubscriptionCount ?? 0;

        internal State ConnectionState => (State)state;
        internal bool IsBeating => Volatile.Read(ref beating) == 1;

        internal long OperationCount => Volatile.Read(ref operationCount);

        public RedisCommand LastCommand { get; private set; }

        /// <summary>
        /// If we have (or had) a connection, report the protocol being used.
        /// </summary>
        /// <remarks>The value remains after disconnect, so that appropriate follow-up actions (pub/sub etc) can work reliably.</remarks>
        public RedisProtocol? Protocol => _protocol == 0 ? default(RedisProtocol?) : _protocol;
        private RedisProtocol _protocol; // note starts at zero, not RESP2
        internal void SetProtocol(RedisProtocol protocol) => _protocol = protocol;

        /// <summary>
        /// Indicates whether the bridge needs to reconnect.
        /// </summary>
        internal bool NeedsReconnect => Volatile.Read(ref _needsReconnect);

        /// <summary>
        /// Marks that the bridge needs to reconnect.
        /// </summary>
        internal void MarkNeedsReconnect() => Volatile.Write(ref _needsReconnect, true);

        /// <summary>
        /// Where a maintenance handoff stands on this bridge. One variable, advanced in one direction, and the only
        /// thing every handoff question is answered from: whether new work is held, whether the backlog may flush,
        /// whether a failure is the handoff's own, and whether work enqueued now is covered. (Parallel flags for the
        /// same facts kept disagreeing with each other in the races between them.)
        /// </summary>
        private enum HandoffPhase
        {
            /// <summary>
            /// The connection current when the hold began (the target) is being drained: new caller work is held in
            /// the backlog, the backlog does not flush, and the drain is waiting for the target's in-flight work.
            /// </summary>
            Holding,

            /// <summary>
            /// The target is gone - retired by the drain, or it went away by itself - and whatever is current is not
            /// yet known to be a bona fide replacement. New work is still held and the backlog still does not flush,
            /// because writing to a connection the server is about to close would only hand it work to fail.
            /// </summary>
            AwaitingReplacement,

            /// <summary>
            /// A bona fide replacement is current and established, and the held work is being written to it. The
            /// handoff's protection stands until the last held message has been written: a failure before that would
            /// still abandon them under a FailFast policy.
            /// </summary>
            Flushing,

            /// <summary>Nothing left to protect; or the deadline passed; or the bridge was disposed.</summary>
            Done,
        }

        /// <summary>
        /// One maintenance handoff on this bridge: its phase, and the facts fixed when it began. A new object per
        /// handoff, so that a transition requested against an earlier handoff (a backlog pass finishing an earlier
        /// flush, say) cannot land on a later one.
        /// </summary>
        private sealed class Handoff
        {
            private int _phase; // HandoffPhase
            private int _retiring; // a retire of the target, outside the drain loop, is under way
            private PhysicalConnection? _target;

            /// <summary>
            /// Until when this handoff stands at the latest: the hold, the rest of the window and one connect attempt,
            /// from when the hold began. This handoff's own, not shared with an earlier one: held work is stamped with
            /// it, so the time a command can be in limbo is bounded by the handoff that held it, never by a longer one
            /// that happened to come before. It is read on every question put to a live handoff, so a passed deadline
            /// is noticed while it is still fresh.
            /// </summary>
            public Maintenance.TickDeadline Deadline;

            /// <summary>The connection the hold began on, or a reconnect adopted in its place; null if the bridge had none.</summary>
            public PhysicalConnection? Target => Volatile.Read(ref _target);

            /// <summary>
            /// Adopts a reconnect that landed back on the node being retired as the target, once: of two callers that
            /// see the same doomed connection (its establishment and a heartbeat, say) one adopts and retires it.
            /// </summary>
            public bool TryAdopt(PhysicalConnection doomed)
            {
                var previous = Volatile.Read(ref _target);
                if (ReferenceEquals(previous, doomed) || Interlocked.CompareExchange(ref _target, doomed, previous) != previous) return false;
                doomed.MarkHandoffTarget();
                return true;
            }

            /// <summary>The successor this handoff pinned reconnection to, if the server named one; a connection dialled through it is the replacement.</summary>
            public readonly System.Net.EndPoint? Successor;

            /// <summary>Whether this handoff retired a connection (rather than finding it gone).</summary>
            public volatile bool Retired;

            /// <summary>The address and port of the node being retired, as seen from the connection that was told; null if unreadable.</summary>
            public readonly System.Net.IPEndPoint? RetiringEndPoint;

            /// <summary>
            /// Whether a socket's remote address says which node it reached. Only when the handoff has itself seen a
            /// second address - the server named a successor, or DNS moved to a new address - and there is no tunnel
            /// in the way: then a reconnect reporting the retiring address is back on the retiring node. Behind a
            /// tunnel, or when the only address ever involved is the one in front of the cluster, every connection
            /// reports the same address and comparing it would brand the genuine replacement doomed forever; then
            /// only time tells.
            /// </summary>
            public readonly bool AddressIdentifiesNode;

            public Handoff(PhysicalConnection? target, System.Net.IPEndPoint? retiringEndPoint, System.Net.EndPoint? successor, bool addressIdentifiesNode, TimeSpan protection)
            {
                _target = target;
                RetiringEndPoint = retiringEndPoint;
                Successor = successor;
                AddressIdentifiesNode = addressIdentifiesNode;
                Deadline.SetIn(protection);
            }

            /// <summary>Claims the one-at-a-time retire of the target outside the drain loop; see <see cref="PhysicalBridge.Judge"/>.</summary>
            public bool TryBeginRetire() => Interlocked.CompareExchange(ref _retiring, 1, 0) == 0;

            public void EndRetire() => Volatile.Write(ref _retiring, 0);

            public HandoffPhase Phase => (HandoffPhase)Volatile.Read(ref _phase);

            /// <summary>Advances the phase, if it is still where the caller saw it.</summary>
            public bool TryAdvance(HandoffPhase from, HandoffPhase to)
                => Interlocked.CompareExchange(ref _phase, (int)to, (int)from) == (int)from;

            /// <summary>Ends the handoff whatever its phase: the deadline passed, or the bridge was disposed.</summary>
            public void Finish() => Volatile.Write(ref _phase, (int)HandoffPhase.Done);
        }

        private volatile Handoff? _handoff;

        /// <summary>The handoff in progress, if there is one and its deadline has not passed (a passed deadline ends it).</summary>
        private bool TryGetHandoff([NotNullWhen(true)] out Handoff? handoff)
        {
            handoff = _handoff;
            if (handoff is null || handoff.Phase == HandoffPhase.Done) return false;
            if (handoff.Deadline.IsActive) return true;
            handoff.Finish();
            return false;
        }

        private HandoffPhase Phase => TryGetHandoff(out var handoff) ? handoff.Phase : HandoffPhase.Done;

        /// <summary>
        /// A handoff's hold is in force: whatever is current is the target, or is not known to be a bona fide
        /// replacement. New work is queued and the backlog does not flush; the moment a replacement is current and
        /// established (or the deadline passes), the hold is over and the backlog flushes to it.
        /// </summary>
        private bool HoldUnresolved => Phase is HandoffPhase.Holding or HandoffPhase.AwaitingReplacement;

        /// <summary>
        /// The last resort when the write lock could not be had for the whole of the drain's deadline: retires the
        /// target without it. A writer wedged on the lock is the only thing this can race, and what it risks is that
        /// one writer's message failing with a write fault; what it buys is the replacement starting now rather than
        /// at the server's window-end close, which would fail everything in flight and leave the bridge dark for the
        /// reconnect. Before handoffs existed the recycle was always lockless, so this is the old behaviour kept as
        /// the fallback rather than the rule.
        /// </summary>
        internal bool RetireTargetWithoutLock(string reason)
        {
            if (!TryGetHandoff(out var handoff) || handoff.Target is not { } target || !ReferenceEquals(physical, target)) return false;
            if (!target.TryMarkRetiredForHandoff()) return false; // a retire under the lock got there first; its teardown follows
            handoff.Retired = true;
            handoff.TryAdvance(HandoffPhase.Holding, HandoffPhase.AwaitingReplacement);
            RetireConnection(target, reason + " (without the write lock: last resort)");
            return true;
        }

        /// <summary>
        /// The rules the handoff keeps, stated once so that the next edit is checked against them rather than
        /// against the last edit:
        /// <list type="bullet">
        /// <item>(a) protection stands down only when the held work is <em>safe</em> - written to a bona fide
        /// replacement, or past the deadline that also bounds its cover - never merely because a replacement
        /// exists;</item>
        /// <item>(b) classifying a connection has three outcomes, replacement, doomed or unknown, and unknown always
        /// defers to a later pass - it is never acted on as either of the others;</item>
        /// <item>(c) what a connection <em>is</em> - the target, made under the successor pin, established - is
        /// recorded on the connection when it happens and read from there; the handoff's phase says only where the
        /// flow stands, and nothing about a connection is inferred from it.</item>
        /// </list>
        /// </summary>
        private enum Replacement
        {
            /// <summary>A bona fide replacement: not the target, and demonstrably not back on the node being retired.</summary>
            Yes,

            /// <summary>The target, or a reconnect that landed back on the node being retired: the server will close it.</summary>
            Doomed,

            /// <summary>Not classifiable yet (its address cannot be read, or only time can tell and it has not yet): defer.</summary>
            Unknown,
        }

        /// <summary>
        /// Classifies a connection against a handoff, from the connection's own facts (rule c). The target, or a
        /// reconnect adopted as one, is doomed whatever else is true of it. A connection dialled through <em>this</em>
        /// handoff's successor pin went where the server said to go, and is the replacement even if it reports the
        /// retiring address: a proxy restarted in place keeps its address, and the server naming it as the successor
        /// is the authority on that. (A stale pin from an earlier handoff confers nothing, since it is matched against
        /// this handoff's successor; and a handoff that names no successor drops any earlier pin.) Otherwise, where the
        /// remote address identifies the node and could be read, it decides: back on the retiring address is doomed
        /// and anywhere else is the replacement. Where the address identifies nothing (a tunnel, a fixed front
        /// address, a successor at the same address) only time tells: while the target is still being drained a
        /// different current connection cannot be judged; once the target is gone, nothing else will arrive, so what is
        /// current is the replacement. What is left - an address that could not be read yet, or nothing to compare it
        /// against because the connection that was told had no readable address - is unknown and defers to a later
        /// pass (rule b): a verdict of replacement needs positive evidence, never a default, since the alternative is
        /// flushing held work onto a stale-DNS reconnect to the node being retired. The cost is that a connection
        /// nothing can ever judge is held to the deadline.
        /// </summary>
        private static Replacement Classify(Handoff handoff, PhysicalConnection connection)
        {
            if (connection.WasHandoffTarget) return Replacement.Doomed;
            if (connection.HandoffTargetDialed is { } dialed && handoff.Successor is { } successor && dialed.Equals(successor))
            {
                return Replacement.Yes;
            }
            var retiring = handoff.RetiringEndPoint;
            if (handoff.AddressIdentifiesNode && retiring is not null && connection.TryGetRemoteEndPoint() is { } landedOn)
            {
                return landedOn.Equals(retiring) ? Replacement.Doomed : Replacement.Yes;
            }
            if (!handoff.AddressIdentifiesNode)
            {
                return handoff.Phase == HandoffPhase.Holding ? Replacement.Unknown : Replacement.Yes;
            }
            return Replacement.Unknown;
        }

        /// <summary>
        /// Whether the backlog still holds work a handoff diverted, including a message the processor has dequeued but
        /// not yet written; the protection stands until it has been written (rule a). Under the backlog lock, which is
        /// where the processor moves a message from the queue to its hand, so the message is seen in one place or the
        /// other. Only the termination path asks this, never the per-message path.
        /// </summary>
        private bool AnyHeldQueued()
        {
            lock (_backlog)
            {
                if (_backlogInHand is { HeldForHandoff: true }) return true;
                foreach (var message in _backlog)
                {
                    if (message.HeldForHandoff) return true;
                }
                return false;
            }
        }

        /// <summary>The message the backlog processor has taken off the queue and not yet finished writing; see <see cref="AnyHeldQueued"/>.</summary>
        private volatile Message? _backlogInHand;

        /// <summary>
        /// Whether a connection failure belongs to a maintenance handoff in progress: the target (retired by the
        /// drain, closed by the server at the end of the window, or dropped by itself), a reconnect that landed back
        /// on the node being retired, or an attempt to bring up the replacement that never completed its handshake,
        /// whatever the failure type. Only a replacement that was fully established and then failed is an ordinary
        /// failure, and a FailFast policy applies to it as usual, even while the handoff is still flushing.
        /// </summary>
        private bool IsHandoffOwnFailure(PhysicalConnection connection)
            => TryGetHandoff(out var handoff)
                && (!connection.EverFullyEstablished || Classify(handoff, connection) != Replacement.Yes);

        /// <summary>
        /// Starts holding caller work back from the current connection while a maintenance handoff lets what has
        /// already been written complete; see <see cref="ServerEndPoint.DrainThenRecycleAsync"/>. The connection
        /// current at this moment is the one the handoff will retire, and the only one it will ever retire: a
        /// replacement established meanwhile is left alone.
        /// </summary>
        /// <remarks>
        /// Held work goes to the backlog rather than the socket, and is written to the replacement connection once
        /// that is established. Without this the drain could never finish under steady traffic: there was always
        /// something in flight, so the recycle waited for the end of the announced window and then failed whatever
        /// was in flight at that instant.
        /// </remarks>
        /// <param name="protection">For how long at most the handoff stands - the FailFast shield here and the timeout
        /// cover on the held commands - if no bona fide replacement establishes and flushes first.</param>
        /// <param name="retiringEndPoint">The address and port of the node being retired, as seen from the connection
        /// that was told, if known; see <see cref="Classify"/>.</param>
        /// <param name="successor">The successor the server named and reconnection is pinned to, if any.</param>
        /// <param name="addressIdentifiesNode">Whether a socket's remote address identifies the node it reached; see
        /// <see cref="Handoff.AddressIdentifiesNode"/>.</param>
        internal void BeginHold(TimeSpan protection, System.Net.IPEndPoint? retiringEndPoint, System.Net.EndPoint? successor, bool addressIdentifiesNode)
        {
            var target = physical;
            target?.MarkHandoffTarget();
            // Publish the new handoff, then finish the previous one - in that order, so that there is no instant at
            // which neither hold is in force: a wake racing this would otherwise flush the previous hold's work to the
            // very connection this hold is about to drain. The previous handoff (still flushing, say) is finished
            // explicitly rather than left mid-phase: its replacement is the connection this one retires, so its flush
            // cannot complete, and its held work - which belongs to the bridge's backlog, not to a handoff - keeps its
            // stamped cover and is flushed with this handoff's.
            var previous = _handoff;
            var handoff = new Handoff(target, retiringEndPoint, successor, addressIdentifiesNode, protection);
            _handoff = handoff;
            previous?.Finish();

            // Work already in the backlog when the hold begins is delayed by it exactly as diverted work is (the
            // processor does not flush while the hold is unresolved), so it gets the same cover: without this, a
            // queue that predates the MOVING would be timed out by the hold itself under short timeouts with
            // relaxation off. Store the handoff, fence, then walk the queue - paired with BacklogEnqueue, which
            // enqueues, fences, then re-reads the handoff - so a message enqueued concurrently is stamped by one side
            // or the other.
            Interlocked.MemoryBarrier();
            foreach (var queued in _backlog)
            {
                if (!queued.IsInternalCall) queued.MarkHeldForHandoff(handoff.Deadline.Ticks);
            }
        }

        /// <summary>
        /// The drain loop has ended, whatever it achieved. A target it could not retire (the lock never came) is left
        /// to the server's close, and the hold stays until a replacement is current; a connection that established
        /// while the drain was still judging the target is judged again now that the target is gone.
        /// </summary>
        internal void EndDrain()
        {
            if (!TryGetHandoff(out var handoff)) return;
            handoff.TryAdvance(HandoffPhase.Holding, HandoffPhase.AwaitingReplacement);
            ReevaluateHandoff();
        }

        /// <summary>
        /// Judges the current connection against the handoff in progress, if it is established. Run at establishment,
        /// when the drain loop ends, and from every heartbeat: the drain loop and the establishment callback each see
        /// one instant, and a phase must not be wedged by what that instant happened to miss - a poll landing in the
        /// gap between a disconnect and the reconnect, or an address that could not be read at the moment of
        /// establishment (rule b: unknown defers to a later pass, and the heartbeat is that pass).
        /// </summary>
        private void ReevaluateHandoff()
        {
            if (!TryGetHandoff(out var handoff) || handoff.Phase is HandoffPhase.Flushing or HandoffPhase.Done) return;
            if (physical is { EverFullyEstablished: true, RetiredForHandoff: false } current) Judge(handoff, current);
        }

        /// <summary>
        /// An established connection, judged: a bona fide replacement resolves the hold; a doomed one, once the drain
        /// loop is over, is retired - a reconnect that landed back on the node being retired (adopted first, as the
        /// drain loop would have adopted it), or the target itself, still current because the loop's retire never got
        /// the lock or faulted. The retire takes the write lock, so it runs apart from the caller (an establishment
        /// callback, or the heartbeat), one at a time and idempotent under the lock: a heartbeat that finds the
        /// previous attempt still waiting does not pile on, and one that finds it failed tries again. While the target
        /// is still being drained, the drain loop owns the judgement.
        /// </summary>
        private void Judge(Handoff handoff, PhysicalConnection connection)
        {
            switch (Classify(handoff, connection))
            {
                case Replacement.Yes:
                    OnReplacementEstablished(handoff);
                    break;
                case Replacement.Doomed when handoff.Phase == HandoffPhase.AwaitingReplacement:
                    if ((ReferenceEquals(connection, handoff.Target) || handoff.TryAdopt(connection)) && handoff.TryBeginRetire())
                    {
                        _ = RetireTargetAsync(handoff);
                    }
                    break;
            }
        }

        /// <summary>
        /// The retire outside the drain loop. Forced: the loop's own forced pass has come and gone by the time this
        /// runs, and nothing a caller is waiting on can be in flight on a reconnect that the hold has diverted caller
        /// work away from since before it was connected.
        /// </summary>
        private async Task RetireTargetAsync(Handoff handoff)
        {
            try
            {
                using var lockDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await TryRetireTargetAsync(force: true, "still on the node being retired after the drain", lockDeadline.Token).ForAwait();
            }
            catch (Exception ex)
            {
                OnInternalError(ex);
            }
            finally
            {
                handoff.EndRetire();
            }
        }

        /// <summary>
        /// A bona fide replacement is current and established: the hold is over and the backlog flushes to it. The
        /// handoff stands until the held work has been written (rule a); if nothing is held, it is done now.
        /// </summary>
        private void OnReplacementEstablished(Handoff handoff)
        {
            if (!(handoff.TryAdvance(HandoffPhase.Holding, HandoffPhase.Flushing)
                || handoff.TryAdvance(HandoffPhase.AwaitingReplacement, HandoffPhase.Flushing)))
            {
                return; // already flushing, or done
            }
            TryFinishFlush(handoff);
            // Store the phase, fence, then load the queue - the mirror image of WakeIfHoldReleased, which every held
            // enqueue (single, batch, requeue) goes through. With a full fence on both sides at least one of them
            // sees the other's store, so a message held concurrently with the release is not stranded; neither side
            // starts a processor for an empty backlog, and StartBacklogProcessor wakes one that is already running.
            Interlocked.MemoryBarrier();
            if (BacklogHasItems) StartBacklogProcessor();
        }

        /// <summary>
        /// The held work has all been written: nothing left to protect (rule a). A message held between the check and
        /// the transition (a requeue by a writer that reached the lock late) keeps its cover - that is stamped on the
        /// message - and is flushed by the processor like any other; only the FailFast shield is down for it.
        /// </summary>
        private void TryFinishFlush(Handoff handoff)
        {
            if (handoff.Phase == HandoffPhase.Flushing && !AnyHeldQueued()) handoff.TryAdvance(HandoffPhase.Flushing, HandoffPhase.Done);
        }

        /// <summary>
        /// Whether a message should be held in the backlog for the replacement connection rather than written: only
        /// while a handoff is draining a connection that is still live (once it has dropped, the ordinary disconnected
        /// rules and the backlog policy apply), and only for a caller's work. Internal traffic (keep-alives, probes)
        /// still goes to the old connection, and so does QUIT, which is never queued.
        /// </summary>
        private bool ShouldHoldForHandoff(Message message) => HoldActive && IsHoldable(message);

        /// <summary>A handoff's hold is unresolved, and the current connection is live.</summary>
        private bool HoldActive => HoldUnresolved && IsConnected && !NeedsReconnect;

        /// <summary>A caller's work, and not QUIT, which is never queued.</summary>
        private static bool IsHoldable(Message message) => !message.IsInternalCall && message.Command != RedisCommand.QUIT;

        /// <summary>
        /// A batch is written as a unit, so it is held as a unit: when it carries any holdable message and no QUIT.
        /// An internal member rides along with the rest, because splitting the batch would be worse than holding it.
        /// </summary>
        private static bool ShouldHoldBatch(List<Message> messages)
        {
            bool any = false;
            foreach (var message in messages)
            {
                if (message.Command == RedisCommand.QUIT) return false;
                any |= IsHoldable(message);
            }
            return any;
        }

        /// <summary>
        /// Retires the connection the hold began on, under the write lock, once nothing a caller is waiting on is in
        /// flight on it - or regardless, when <paramref name="force"/> says the drain has waited long enough. If that
        /// connection has gone by itself and what is current is a reconnect that landed back on the node being retired
        /// (no successor pinned, DNS still naming the old node), that reconnect takes its place and is retired instead;
        /// a bona fide replacement is left alone. The lock is what keeps a writer that passed the hold check before it
        /// was set from slipping onto the old connection between the check and the retire; such a writer finds the
        /// connection retired when it gets the lock, and queues for the replacement instead (see
        /// <see cref="TryRequeueForHandoff"/>).
        /// </summary>
        /// <param name="force">Replace the connection even if caller work is still in flight on it.</param>
        /// <param name="reason">Why; for the trace.</param>
        /// <param name="lockDeadline">Bounds the wait for the write lock (together with the mutex's own timeout, whichever
        /// ends first); the drain re-waits until this deadline, so it is the deadline that decides how long overall.</param>
        /// <returns><c>null</c> if the connection could not be retired yet - caller work in flight, the lock not
        /// obtained inside the deadline, or the current connection not classifiable yet - so the drain should try
        /// again; otherwise whether this handoff retired a connection.</returns>
        internal async ValueTask<bool?> TryRetireTargetAsync(bool force, string reason, CancellationToken lockDeadline)
        {
            if (!TryGetHandoff(out var handoff)) return false;
            if (handoff.Phase is HandoffPhase.Flushing or HandoffPhase.Done) return handoff.Retired;

            // Lock-free first: the drain polls this every 20ms, and only a pass that is going to retire needs the write
            // lock (held across the check and the marking). The in-flight question needs only the connection's own
            // lock; and with the target gone, finding the bridge disconnected, a replacement current, or a connection
            // not yet classifiable settles the pass without a retire, so without the lock. The locked pass below
            // repeats the judgement on what is current then, which is the one that counts.
            var current = physical;
            if (handoff.Target is { } drainTarget && ReferenceEquals(current, drainTarget))
            {
                if (!force && drainTarget.HasCallerMessagesAwaitingResponse()) return null;
            }
            else
            {
                handoff.TryAdvance(HandoffPhase.Holding, HandoffPhase.AwaitingReplacement);
                if (current is null) return false;
                switch (Classify(handoff, current))
                {
                    case Replacement.Yes:
                        if (current.EverFullyEstablished) OnReplacementEstablished(handoff);
                        return false;
                    case Replacement.Unknown:
                        return null;
                }
            }

            PhysicalConnection toRetire;
            bool gotLock;
            try
            {
                gotLock = await _singleWriter.TryTakeAsync(lockDeadline).ForAwait();
            }
            catch (OperationCanceledException)
            {
                gotLock = false;
            }
            catch (ObjectDisposedException)
            {
                // the bridge was disposed mid-handoff: nothing left to retire, and no replacement is coming
                handoff.Finish();
                return false;
            }
            if (!gotLock) return null;
            try
            {
                if (handoff.Phase is HandoffPhase.Flushing or HandoffPhase.Done) return handoff.Retired;
                current = physical;
                if (handoff.Target is { } target && ReferenceEquals(current, target))
                {
                    toRetire = target;
                }
                else
                {
                    // The connection the hold began on is gone - it went away by itself (the server closed it, or it
                    // faulted), or there was none when the hold began because the bridge was already disconnected.
                    // The phase says so first, so that whatever is current is judged as of a target that is gone.
                    handoff.TryAdvance(HandoffPhase.Holding, HandoffPhase.AwaitingReplacement);
                    if (current is null)
                    {
                        // reconnecting: the reconnect is judged when it establishes, and whatever the hold diverted
                        // stays protected for it until then
                        return false;
                    }
                    switch (Classify(handoff, current))
                    {
                        case Replacement.Yes:
                            // the reconnect is the replacement (a pinned successor, or demonstrably another node):
                            // retiring it would only force a second connect cycle. If it is already up, the hold is
                            // over now; if it is still connecting, its establishment will see to that.
                            if (current.EverFullyEstablished) OnReplacementEstablished(handoff);
                            return false;
                        case Replacement.Unknown:
                            // not classifiable yet - a socket still connecting, say (rule b): never retired on a guess,
                            // never branded a target; the next pass looks again, and the drain's deadline bounds this
                            return null;
                        default:
                            // back on the node being retired: what is current now is what the handoff replaces -
                            // unless its establishment or a heartbeat adopted it first, in which case that retire is
                            // under way and the next pass sees its result
                            if (!handoff.TryAdopt(current)) return null;
                            toRetire = current;
                            break;
                    }
                }
                if (toRetire.RetiredForHandoff) return handoff.Retired; // already retired by another path; its teardown follows that path's lock release
                if (!force && toRetire.HasCallerMessagesAwaitingResponse()) return null;

                // Under the lock only the marking: from here no writer that takes the lock writes to the connection (see
                // TryRequeueForHandoff), and the backlog processor skips it too, so the teardown itself can happen
                // after the release rather than stalling every writer behind event handlers and socket shutdown. The
                // marking is a compare-exchange, so of this and a lockless last-resort retire racing it, exactly one
                // tears the connection down.
                if (!toRetire.TryMarkRetiredForHandoff()) return handoff.Retired;
                handoff.Retired = true;
                handoff.TryAdvance(HandoffPhase.Holding, HandoffPhase.AwaitingReplacement);
            }
            finally
            {
                try
                {
                    _singleWriter.Release();
                }
                catch (ObjectDisposedException)
                {
                    // disposed while we held it; nobody is waiting on a disposed mutex
                }
            }

            RetireConnection(toRetire, reason);
            return true;
        }

        /// <summary>
        /// Tears down a connection a handoff has marked retired. Report <em>before</em> tearing down, and in this
        /// order for a reason: <c>RecordConnectionFailed</c> only raises the public event while the pipe is still live
        /// ("if *we* didn't burn the pipe: flag it"), and <c>Dispose</c> runs <c>Shutdown</c> first - which is exactly
        /// why an ordinary dispose is silent. Recording first also performs the disconnect, so the dispose is cleanup.
        /// A user handler that throws must not leak the connection, nor make the retire look as if it had not happened.
        /// </summary>
        private void RetireConnection(PhysicalConnection connection, string reason)
        {
            Trace($"recycling {ConnectionType} connection: {reason}");
            try
            {
                connection.RecordConnectionFailed(ConnectionFailureType.MaintenanceHandoff);
            }
            catch (Exception ex)
            {
                OnInternalError(ex);
            }
            finally
            {
                try
                {
                    connection.Dispose();
                }
                catch (Exception ex)
                {
                    OnInternalError(ex);
                }
            }
        }

        public void Dispose()
        {
            isDisposed = true;
            _handoff?.Finish(); // nothing left to protect, and no replacement is coming
            // If there's anything in the backlog and we're being torn down - exfil it immediately (e.g. so all awaitables complete)
            AbandonPendingBacklog(new ObjectDisposedException("Connection is being disposed"), null);
            try
            {
                _backlogAutoReset?.Set();
                _backlogAutoReset?.Dispose();
            }
            catch { }
            using (var tmp = physical)
            {
                physical = null;
            }
            GC.SuppressFinalize(this);
        }

        ~PhysicalBridge()
        {
            isDisposed = true; // make damn sure we don't true to resurrect

            // If there's anything in the backlog and we're being torn down - exfil it immediately (e.g. so all awaitables complete)
            AbandonPendingBacklog(new ObjectDisposedException("Connection is being finalized"), null, locked: false);

            // shouldn't *really* touch managed objects
            // in a finalizer, but we need to kill that socket,
            // and this is the first place that isn't going to
            // be rooted by the socket async bits
            try
            {
                var tmp = physical;
                physical = null;
                tmp?.Shutdown();
            }
            catch { }
        }
        public void ReportNextFailure()
        {
            reportNextFailure = true;
        }

        public override string ToString() => ConnectionType + "/" + Format.ToString(ServerEndPoint.EndPoint);

        private WriteResult QueueOrFailMessage(Message message)
        {
            // If it's an internal call that's not a QUIT
            // or we're allowed to queue in general, then queue
            if (message.IsInternalCall || Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
            {
                // Let's just never ever queue a QUIT message
                if (message.Command != RedisCommand.QUIT)
                {
                    message.SetEnqueued(null);
                    BacklogEnqueue(message);
                    // Note: we don't start a worker on each message here
                    return WriteResult.Success; // Successfully queued, so indicate success
                }
            }

            // Anything else goes in the bin - we're just not ready for you yet
            return FailDueToNoConnection(message);
        }

        private WriteResult FailDueToNoConnection(Message message)
        {
            // fail with the exception the caller would otherwise construct from the result, rather than Cancel():
            // a cancelled box surfaces as TaskCanceledException, and although ExecuteAsyncImpl then tries to
            // fault the task with the real exception, the box's continuation can get there first (#3283) - and a
            // batch has no caller translating the result at all
            var ex = ExceptionFactory.NoConnectionAvailable(Multiplexer, message, ServerEndPoint);
            Multiplexer.OnMessageFaulted(message, ex);
            message.SetExceptionAndComplete(ex, null);
            return WriteResult.NoConnectionAvailable;
        }

        [Obsolete("prefer async")]
        public WriteResult TryWriteSync(Message message, bool isReplica)
        {
            if (isDisposed) throw new ObjectDisposedException(Name);
            if (ShouldHoldForHandoff(message))
            {
                EnqueueHeldAndWakeIfReleased(message);
                return WriteResult.Success; // queued counts as success
            }
            if (!IsConnected || NeedsReconnect) return QueueOrFailMessage(message);

            var physical = this.physical;
            if (physical == null)
            {
                // If we're not connected yet and supposed to, queue it up
                if (Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    if (TryPushToBacklog(message, onlyIfExists: false))
                    {
                        message.SetEnqueued(null);
                        return WriteResult.Success;
                    }
                }
                return FailDueToNoConnection(message);
            }
            var result = WriteMessageTakingWriteLockSync(physical, message);
            LogNonPreferred(message.Flags, isReplica);
            return result;
        }

        public ValueTask<WriteResult> TryWriteAsync(Message message, bool isReplica, bool bypassBacklog = false)
        {
            if (isDisposed) throw new ObjectDisposedException(Name);
            if (!bypassBacklog && ShouldHoldForHandoff(message))
            {
                EnqueueHeldAndWakeIfReleased(message);
                return new ValueTask<WriteResult>(WriteResult.Success); // queued counts as success
            }
            if ((!IsConnected || NeedsReconnect) && !bypassBacklog) return new ValueTask<WriteResult>(QueueOrFailMessage(message));

            var physical = this.physical;
            if (physical == null)
            {
                // If we're not connected yet and supposed to, queue it up
                if (!bypassBacklog && Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    if (TryPushToBacklog(message, onlyIfExists: false))
                    {
                        message.SetEnqueued(null);
                        return new ValueTask<WriteResult>(WriteResult.Success);
                    }
                }
                return new ValueTask<WriteResult>(FailDueToNoConnection(message));
            }

            var result = WriteMessageTakingWriteLockAsync(physical, message, bypassBacklog: bypassBacklog);
            LogNonPreferred(message.Flags, isReplica);
            return result;
        }

        internal void AppendProfile(StringBuilder sb)
        {
            var clone = new long[ProfileLogSamples + 1];
            for (int i = 0; i < ProfileLogSamples; i++)
            {
                clone[i] = Volatile.Read(ref profileLog[i]);
            }
            clone[ProfileLogSamples] = Volatile.Read(ref operationCount);
            Array.Sort(clone);
            sb.Append(' ').Append(clone[0]);
            for (int i = 1; i < clone.Length; i++)
            {
                if (clone[i] != clone[i - 1])
                {
                    sb.Append('+').Append(clone[i] - clone[i - 1]);
                }
            }
            if (clone[0] != clone[ProfileLogSamples])
            {
                sb.Append('=').Append(clone[ProfileLogSamples]);
            }
            double rate = (clone[ProfileLogSamples] - clone[0]) / ProfileLogSeconds;
            sb.Append(" (").Append(rate.ToString("N2")).Append(" ops/s; spans ").Append(ProfileLogSeconds).Append("s)");
        }

        internal void GetCounters(ConnectionCounters counters)
        {
            counters.OperationCount = OperationCount;
            counters.SocketCount = Volatile.Read(ref socketCount);
            counters.WriterCount = Volatile.Read(ref activeWriters);
            counters.NonPreferredEndpointCount = Volatile.Read(ref nonPreferredEndpointCount);
            counters.PendingUnsentItems = Volatile.Read(ref _backlogCurrentEnqueued);
            physical?.GetCounters(counters);
        }

        internal readonly struct BridgeStatus
        {
            /// <summary>
            /// Number of messages sent since the last heartbeat was processed.
            /// </summary>
            public int MessagesSinceLastHeartbeat { get; init; }

            /// <summary>
            /// The time this connection was connected at, if it's connected currently.
            /// </summary>
            public DateTime? ConnectedAt { get; init; }

            /// <summary>
            /// Whether the pipe writer is currently active.
            /// </summary>
            public bool IsWriterActive { get; init; }

            /// <summary>
            /// Status of the currently processing backlog, if any.
            /// </summary>
            public BacklogStatus BacklogStatus { get; init; }

            /// <summary>
            /// The number of messages that are in the backlog queue (waiting to be sent when the connection is healthy again).
            /// </summary>
            public int BacklogMessagesPending { get; init; }

            /// <summary>
            /// The number of messages that are in the backlog queue (waiting to be sent when the connection is healthy again).
            /// </summary>
            public int BacklogMessagesPendingCounter { get; init; }

            /// <summary>
            /// The number of messages ever added to the backlog queue in the life of this connection.
            /// </summary>
            public long TotalBacklogMessagesQueued { get; init; }

            /// <summary>
            /// Status for the underlying <see cref="PhysicalConnection"/>.
            /// </summary>
            public PhysicalConnection.ConnectionStatus Connection { get; init; }

            /// <summary>
            /// The default bridge stats, notable *not* the same as <c>default</c> since initializers don't run.
            /// </summary>
            public static BridgeStatus Zero { get; } = new() { Connection = PhysicalConnection.ConnectionStatus.Zero };

            public override string ToString() =>
                $"MessagesSinceLastHeartbeat: {MessagesSinceLastHeartbeat}, ConnectedAt: {ConnectedAt?.ToString("u") ?? "n/a"}, Writer: {(IsWriterActive ? "Active" : "Inactive")}, BacklogStatus: {BacklogStatus}, BacklogMessagesPending: (Queue: {BacklogMessagesPending}, Counter: {BacklogMessagesPendingCounter}), TotalBacklogMessagesQueued: {TotalBacklogMessagesQueued}, Connection: ({Connection})";
        }

        internal BridgeStatus GetStatus() => new()
        {
            MessagesSinceLastHeartbeat = (int)(Volatile.Read(ref operationCount) - Volatile.Read(ref profileLastLog)),
            ConnectedAt = ConnectedAt,
            IsWriterActive = !_singleWriter.IsAvailable,
            BacklogMessagesPending = _backlog.Count,
            BacklogMessagesPendingCounter = Volatile.Read(ref _backlogCurrentEnqueued),
            BacklogStatus = _backlogStatus,
            TotalBacklogMessagesQueued = _backlogTotalEnqueued,
            Connection = physical?.GetStatus() ?? PhysicalConnection.ConnectionStatus.Default,
        };

        internal string GetStormLog()
        {
            var sb = new StringBuilder("Storm log for ").Append(Format.ToString(ServerEndPoint.EndPoint)).Append(" / ").Append(ConnectionType)
                .Append(" at ").Append(DateTime.UtcNow)
                .AppendLine().AppendLine();
            physical?.GetStormLog(sb);
            sb.Append("Circular op-count snapshot:");
            AppendProfile(sb);
            sb.AppendLine();
            return sb.ToString();
        }

        internal void IncrementOpCount()
        {
            Interlocked.Increment(ref operationCount);
        }

        /// <summary>
        /// Sends a keepalive message (ECHO or PING) to keep connections alive and check validity of response.
        /// </summary>
        /// <param name="forceRun">Whether to run even then the connection isn't idle.</param>
        internal void KeepAlive(bool forceRun = false)
        {
            if (!forceRun && !(physical?.IsIdle() ?? false)) return; // don't pile on if already doing something

            var commandMap = Multiplexer.CommandMap;
            Message? msg = null;
            var features = ServerEndPoint.GetFeatures();
            switch (ConnectionType)
            {
                case ConnectionType.Interactive:
                    msg = ServerEndPoint.GetTracerMessage(false);
                    msg.SetSource(ResultProcessor.Tracer, null);
                    break;
                case ConnectionType.Subscription:
                    // Normally the PING - observed against a 7.0 server, answered with the two-element array
                    // pong rather than +PONG (OnResponseFrame's IsArrayPong is what keeps that out of the
                    // out-of-band path so it still matches this message). The UNSUBSCRIBE fallback is not just
                    // for pre-3.0 servers: the condition also fails when PING is disabled or renamed in the
                    // CommandMap, or fronted by something that does not support it.
                    //
                    // Neither needs SetInternalCall here: the common path below flags whatever the switch
                    // produced, so any tracer added later is covered without remembering to.
                    if (commandMap.IsAvailable(RedisCommand.PING) && features.PingOnSubscriber)
                    {
                        msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.PING);
                        msg.SetForSubscriptionBridge();
                        msg.SetSource(ResultProcessor.Tracer, null);
                    }
                    else if (commandMap.IsAvailable(RedisCommand.UNSUBSCRIBE))
                    {
                        msg = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.UNSUBSCRIBE, RedisChannel.Literal(Multiplexer.UniqueId));
                        msg.SetSource(ResultProcessor.TrackSubscriptions, null);
                    }
                    break;
            }

            if (msg != null)
            {
                msg.SetInternalCall();
                Multiplexer.Trace("Enqueue: " + msg);
                Multiplexer.OnInfoMessage($"heartbeat ({physical?.LastWriteSecondsAgo}s >= {ServerEndPoint.WriteEverySeconds}s, {physical?.GetSentAwaitingResponseCount()} waiting) '{msg.CommandAndKey}' on '{PhysicalName}' (v{features.Version})");
                physical?.UpdateLastWriteTime(); // preemptively
#pragma warning disable CS0618 // Type or member is obsolete
                var result = TryWriteSync(msg, ServerEndPoint.IsReplica);
#pragma warning restore CS0618

                if (result != WriteResult.Success)
                {
                    var ex = Multiplexer.GetException(result, msg, ServerEndPoint);
                    OnInternalError(ex);
                }
            }
        }

        internal async Task OnConnectedAsync(PhysicalConnection connection, ILogger? log)
        {
            Trace("OnConnected");
            if (physical == connection && !isDisposed && ChangeState(State.Connecting, State.ConnectedEstablishing))
            {
                ConnectedAt ??= DateTime.UtcNow;
                await ServerEndPoint.OnEstablishingAsync(connection, log).ForAwait();
                log?.LogInformationOnEstablishingComplete(new(ServerEndPoint.EndPoint));
            }
            else
            {
                try
                {
                    connection.Dispose();
                }
                catch { }
            }
        }

        internal void ResetNonConnected()
        {
            var tmp = physical;
            if (tmp != null && state != (int)State.ConnectedEstablished)
            {
                // we are abandoning any attempt in flight in order to retry; that says nothing about the server, so report
                // it as such (reporting is once-only, so the failure recorded below does not report the attempt again)
                tmp.ReportAttemptOutcome(
                    isSuccess: false,
                    ConnectionFailureType.ConnectionDisposed,
                    new RedisConnectionException(ConnectionFailureType.ConnectionDisposed, CommandFlags.None, "The connection attempt was abandoned so that it could be retried"));
                tmp.RecordConnectionFailed(ConnectionFailureType.UnableToConnect);
            }
            TryConnect(null);
        }

        internal void OnConnectionFailed(PhysicalConnection connection, ConnectionFailureType failureType, Exception innerException, bool wasRequested)
        {
            if (wasRequested)
            {
                Multiplexer.Logger?.LogInformationConnectionFailureRequested(innerException, innerException.Message);
            }
            else
            {
                Multiplexer.Logger?.LogErrorConnectionIssue(innerException, innerException.Message);
            }
            Trace($"OnConnectionFailed: {connection}");
            // If we're configured to, fail all pending backlogged messages - but not for a maintenance handoff, which
            // is a replacement we chose, and not for a failure that is a handoff's own: the backlog then holds work
            // diverted off a live connection, which would have been written and answered, so a death of the old
            // connection (the window expiring, a blip) or a failed attempt to reach the replacement must leave it for
            // the replacement to flush
            if (Multiplexer.RawConfig.BacklogPolicy?.AbortPendingOnConnectionFailure == true && failureType != ConnectionFailureType.MaintenanceHandoff
                && !IsHandoffOwnFailure(connection))
            {
                AbandonPendingBacklog(innerException, connection);
            }

            if (reportNextFailure)
            {
                LastException = innerException;
                reportNextFailure = false; // until it is restored
                var endpoint = ServerEndPoint.EndPoint;
                Multiplexer.OnConnectionFailed(endpoint, ConnectionType, failureType, innerException, reconfigureNextFailure, connection?.ToString());
            }
        }

        internal void OnDisconnected(ConnectionFailureType failureType, PhysicalConnection? connection, out bool isCurrent, out State oldState)
        {
            Trace($"OnDisconnected: {failureType}");

            oldState = default(State); // only defined when isCurrent = true
            ConnectedAt = default;
            if (isCurrent = physical == connection)
            {
                Trace("Bridge noting disconnect from active connection" + (isDisposed ? " (disposed)" : ""));
                oldState = ChangeState(State.Disconnected);
                physical = null;
                if (oldState == State.ConnectedEstablished && !ServerEndPoint.IsReplica)
                {
                    // if the disconnected endpoint was a primary endpoint run info replication
                    // more frequently on it's replica with exponential increments
                    foreach (var r in ServerEndPoint.Replicas)
                    {
                        r.ForceExponentialBackoffReplicationCheck();
                    }
                }
                ServerEndPoint.OnDisconnected(this);

                if (!isDisposed)
                {
                    var consecutive = Interlocked.Increment(ref failConnectCount);
                    if (consecutive == 1)
                    {
                        TryConnect(null); // try to connect immediately
                    }

                    // An endpoint we cannot even connect to is evidence that what we believe about the
                    // deployment may be wrong - and until now nothing acted on that. The reconfigure-on-failure
                    // path is gated on reconfigureNextFailure, which is only ever set once a connection has
                    // been *established*, so a node that has only ever refused could be retried forever
                    // without anybody re-reading the topology. Measured in the field: 37 hours.
                    ServerEndPoint.OnRepeatedConnectFailure(consecutive);
                }
            }
            else if (physical == null)
            {
                Trace("Bridge noting disconnect (already terminated)");
            }
            else
            {
                Trace("Bridge noting disconnect, but from different connection");
            }
        }

        /// <param name="ex">Why.</param>
        /// <param name="connection">The connection, if any, to report against.</param>
        /// <param name="locked">Take the dequeuers' lock, as every head-changing party must while anything else can run;
        /// the finalizer passes <c>false</c>, since it must not block and nothing else is running by then.</param>
        private void AbandonPendingBacklog(Exception ex, PhysicalConnection? connection, bool locked = true)
        {
            while (true)
            {
                Message? next;
                if (locked)
                {
                    lock (_backlog) // the timeout sweep peeks, then dequeues and compares under this lock; a concurrent dequeue here would trip it
                    {
                        if (!BacklogTryDequeue(out next)) break;
                    }
                }
                else if (!BacklogTryDequeue(out next))
                {
                    break;
                }
                // as in PhysicalConnection.RecordMessageFailed: don't hand the same exception to every
                // message. A backlogged message has provably never been written, which is exactly what the
                // retry machinery needs to know, and it is lost if we share the connection-level instance.
                var perMessage = ExceptionFactory.PerMessage(ex, next);
                Multiplexer.OnMessageFaulted(next, perMessage);
                next.SetExceptionAndComplete(perMessage, connection);
            }
        }

        internal void OnFullyEstablished(PhysicalConnection connection, string source)
        {
            Trace("OnFullyEstablished");
            // the attempt itself succeeded (handshake complete), even if this bridge has since moved on and discards it below
            connection.ReportAttemptOutcome(isSuccess: true, ConnectionFailureType.None, null);
            connection.MarkFullyEstablished();
            connection.SetIdle();
            if (physical == connection && !isDisposed && ChangeState(State.ConnectedEstablishing, State.ConnectedEstablished))
            {
                reportNextFailure = reconfigureNextFailure = true;
                LastException = null;
                // only a bona fide replacement establishing ends the hold for this bridge - not the very connection
                // the hold is draining completing a handshake it was in the middle of when the hold began, and not a
                // reconnect that landed back on the node being retired, which is retired in its turn (rule c: the
                // connection's own facts decide)
                if (TryGetHandoff(out var handoff)) Judge(handoff, connection);
                Interlocked.Exchange(ref failConnectCount, 0);
                ServerEndPoint.OnFullyEstablished(connection, source);

                // do we have pending system things to do?
                if (BacklogHasItems)
                {
                    StartBacklogProcessor();
                }

                if (ConnectionType == ConnectionType.Interactive) ServerEndPoint.CheckInfoReplication();
            }
            else
            {
                try { connection.Dispose(); } catch { }
            }
        }

        private int connectStartTicks;
        private long connectTimeoutRetryCount = 0;

        /// <summary>Milliseconds since the current (or most recent) connection attempt began.</summary>
        private int ConnectElapsedMilliseconds() => unchecked(Environment.TickCount - Volatile.Read(ref connectStartTicks));

        private bool DueForConnectRetry()
            => Multiplexer.RawConfig.ReconnectRetryPolicy.ShouldRetry(Volatile.Read(ref connectTimeoutRetryCount), ConnectElapsedMilliseconds());

        internal void OnHeartbeat(bool ifConnectedOnly)
        {
            bool runThisTime = false;
            try
            {
                if (BacklogHasItems)
                {
                    // If we have a backlog, kickoff the processing
                    // This will first timeout any messages that have sat too long and either:
                    // A: Abort if we're still not connected yet (we should be in this path)
                    // or B: Process the backlog and send those messages through the pipe
                    StartBacklogProcessor();
                }

                // a handoff in progress is judged again on every beat, so that no phase is wedged by one missed instant
                ReevaluateHandoff();

                runThisTime = !isDisposed && Interlocked.CompareExchange(ref beating, 1, 0) == 0;
                if (!runThisTime) return;

                uint index = (uint)Interlocked.Increment(ref profileLogIndex);
                long newSampleCount = Volatile.Read(ref operationCount);
                Volatile.Write(ref profileLog[index % ProfileLogSamples], newSampleCount);
                Volatile.Write(ref profileLastLog, newSampleCount);

#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile
                // ReSharper disable once LocalVariableHidesMember
                ref State state = ref Unsafe.As<int, State>(ref this.state);
#pragma warning restore CS0420 // A reference to a volatile field will not be treated as volatile
                Trace("OnHeartbeat: " + state);
                switch (state)
                {
                    case State.Connecting:
                        // The retry policy spaces attempts; it must not cut one short while it is still inside its
                        // ConnectTimeout budget. The default policy (ExponentialRetry at half the connect timeout)
                        // would otherwise abandon an attempt after 2.5s - less than name resolution alone takes
                        // when a DNS server is not answering - and then report it as a connect timeout (#2894).
                        // The socket connect carries its own ConnectTimeout cancellation; this check is what bounds
                        // TLS and anything after it.
                        if (ConnectElapsedMilliseconds() >= Multiplexer.RawConfig.ConnectTimeout && DueForConnectRetry())
                        {
                            Interlocked.Increment(ref connectTimeoutRetryCount);
                            var ex = ExceptionFactory.UnableToConnect(Multiplexer, "ConnectTimeout", Name);
                            LastException = ex;
                            Multiplexer.Logger?.LogErrorConnectionIssue(ex, ex.Message);
                            Trace("Aborting connect");
                            // abort and reconnect
                            var snapshot = physical;
                            snapshot?.ReportAttemptOutcome(isSuccess: false, ConnectionFailureType.UnableToConnect, ex); // else Dispose reports it without the timeout
                            OnDisconnected(ConnectionFailureType.UnableToConnect, snapshot, out bool isCurrent, out State oldState);
                            snapshot?.Dispose(); // Cleanup the existing connection/socket if any, otherwise it will wait reading indefinitely
                            TryConnect(null);
                        }
                        break;
                    case State.ConnectedEstablishing:
                        // (Fall through) Happens when we successfully connected via TCP, but no Redis handshake completion yet.
                        // This can happen brief (usual) or when the server never answers (rare). When we're in this state,
                        // a socket is open and reader likely listening indefinitely for incoming data on an async background task.
                        // We need to time that out and cleanup the PhysicalConnection if needed, otherwise that reader and socket will remain open
                        // for the lifetime of the application due to being orphaned, yet still referenced by the active task doing the pipe read.
                    case State.ConnectedEstablished:
                        var tmp = physical;
                        if (tmp != null)
                        {
                            if (state is State.ConnectedEstablished)
                            {
                                // Track that we should reset the count on the next disconnect, but not do so in a loop, reset
                                // the connect-retry-count (used for backoff decay etc), and remove any non-responsive flag.
                                shouldResetConnectionRetryCount = true;
                                Volatile.Write(ref connectTimeoutRetryCount, 0);
                                tmp.BridgeCouldBeNull?.ServerEndPoint?.ClearUnselectable(UnselectableFlags.DidNotRespond);
                            }
                            tmp.OnBridgeHeartbeat(out int asyncTimeoutThisHeartbeat, out int syncTimeoutThisHeartbeat);
                            int writeEverySeconds = ServerEndPoint.WriteEverySeconds;
                            bool configCheckDue = ServerEndPoint.ConfigCheckSeconds > 0 && ServerEndPoint.LastInfoReplicationCheckSecondsAgo >= ServerEndPoint.ConfigCheckSeconds;

                            if (state is State.ConnectedEstablished && ConnectionType == ConnectionType.Interactive
                                && tmp.BridgeCouldBeNull?.Multiplexer.RawConfig.HeartbeatConsistencyChecks == true)
                            {
                                // If HeartbeatConsistencyChecks are enabled, we're sending a PING (expecting PONG) or ECHO (expecting UniqueID back) every single
                                // heartbeat as an opt-in measure to react to any network stream drop ASAP to terminate the connection as faulted.
                                // If we don't get the expected response to that command, then the connection is terminated.
                                // This is to prevent the case of things like 100% string command usage where a protocol error isn't otherwise encountered.
                                KeepAlive(forceRun: true);

                                // If we're configured to check info replication, perform that too
                                if (configCheckDue)
                                {
                                    ServerEndPoint.CheckInfoReplication();
                                }
                            }
                            else if (state is State.ConnectedEstablished && ConnectionType == ConnectionType.Interactive
                                && configCheckDue
                                && ServerEndPoint.CheckInfoReplication())
                            {
                                // that serves as a keep-alive, if it is accepted
                            }
                            else if (writeEverySeconds > 0 && tmp.LastWriteSecondsAgo >= writeEverySeconds)
                            {
                                Trace("OnHeartbeat - overdue");
                                if (state is State.ConnectedEstablished)
                                {
                                    KeepAlive();
                                }
                                else
                                {
                                    OnDisconnected(ConnectionFailureType.SocketFailure, tmp, out bool ignore, out State oldState);
                                    tmp.Dispose(); // Cleanup the existing connection/socket if any, otherwise it will wait reading indefinitely
                                }
                            }
                            else if (writeEverySeconds <= 0
                                && tmp.IsIdle()
                                && tmp.LastWriteSecondsAgo > 2
                                && tmp.GetSentAwaitingResponseCount() != 0)
                            {
                                // There's a chance this is a dead socket; sending data will shake that up a bit,
                                // so if we have an empty unsent queue and a non-empty sent queue, test the socket.
                                KeepAlive();
                            }

                            // This is an "always" check - we always want to evaluate a dead connection from a non-responsive sever regardless of the need to heartbeat above
                            var totalTimeoutThisHeartbeat = asyncTimeoutThisHeartbeat + syncTimeoutThisHeartbeat;
                            // note these track the *effective* timeout: this heuristic is derived from the
                            // command timeout, so if relaxation says "be patient for 60s" while this says
                            // "dead after 4x5s", we would tear down the connection relaxation was protecting.
                            // Socket-level failure detection is untouched and still notices a dead server.
                            bool deadConnectionOnAsync = asyncTimeoutThisHeartbeat > 0 && tmp.LastReadSecondsAgo * 1_000 > (ServerEndPoint.GetEffectiveTimeoutMilliseconds(tmp.BridgeCouldBeNull?.Multiplexer.AsyncTimeoutMilliseconds ?? 0) * 4);
                            bool deadConnectionOnSync = syncTimeoutThisHeartbeat > 0 && tmp.LastReadSecondsAgo * 1_000 > (ServerEndPoint.GetEffectiveTimeoutMilliseconds(tmp.BridgeCouldBeNull?.Multiplexer.TimeoutMilliseconds ?? 0) * 4);
                            if (deadConnectionOnAsync || deadConnectionOnSync)
                            {
                                // If we've received *NOTHING* on the pipe in 4 timeouts worth of time and we're timing out commands, issue a connection failure so that we reconnect
                                // This is meant to address the scenario we see often in Linux configs where TCP retries will happen for 15 minutes.
                                // To us as a client, we'll see the socket as green/open/fine when writing but we'll bet getting nothing back.
                                // Since we can't depend on the pipe to fail in that case, we want to error here based on the criteria above so we reconnect broken clients much faster.
                                tmp.BridgeCouldBeNull?.Multiplexer.Logger?.LogWarningDeadSocketDetected(tmp.LastReadSecondsAgo, totalTimeoutThisHeartbeat);
                                OnDisconnected(ConnectionFailureType.SocketFailure, tmp, out _, out State oldState);
                                tmp.Dispose(); // Cleanup the existing connection/socket if any, otherwise it will wait reading indefinitely
                            }
                        }
                        break;
                    case State.Disconnected:
                        // Only if we should reset the connection count
                        // This should only happen after a successful reconnection, and not every time we bounce from BeginConnectAsync -> Disconnected
                        // in a failure loop case that happens when a node goes missing forever.
                        if (shouldResetConnectionRetryCount)
                        {
                            shouldResetConnectionRetryCount = false;
                            Volatile.Write(ref connectTimeoutRetryCount, 0);
                        }
                        if (!ifConnectedOnly && DueForConnectRetry())
                        {
                            // Increment count here, so that we don't re-enter in Connecting case up top - we don't want to re-enter and log there.
                            Interlocked.Increment(ref connectTimeoutRetryCount);

                            Multiplexer.Logger?.LogInformationResurrecting(this, connectTimeoutRetryCount);
                            Multiplexer.OnResurrecting(ServerEndPoint.EndPoint, ConnectionType);
                            TryConnect(null);
                        }
                        break;
                    default:
                        Volatile.Write(ref connectTimeoutRetryCount, 0);
                        break;
                }
            }
            catch (Exception ex)
            {
                OnInternalError(ex);
                Trace("OnHeartbeat error: " + ex.Message);
            }
            finally
            {
                if (runThisTime) Interlocked.Exchange(ref beating, 0);
            }
        }

        [Conditional("VERBOSE")]
        internal void Trace(string message) => Multiplexer.Trace(message, ToString());

        [Conditional("VERBOSE")]
        internal void Trace(bool condition, string message)
        {
            if (condition) Multiplexer.Trace(message, ToString());
        }

        internal bool TryEnqueue(List<Message> messages, bool isReplica)
        {
            if (messages == null || messages.Count == 0) return true;

            if (isDisposed) throw new ObjectDisposedException(Name);

            if (HoldActive && ShouldHoldBatch(messages))
            {
                // a maintenance handoff is draining the connection: hold the batch for the replacement
                EnqueueHeldBatchAndWakeIfReleased(messages);
                return true;
            }

            if (!IsConnected || NeedsReconnect)
            {
                // During reconnection (e.g. MOVED-to-same-endpoint), queue messages to the backlog
                // so they can be sent once the connection is re-established.
                // This matches the behavior of TryWriteAsync/TryWriteSync for individual commands.
                if (Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    foreach (var message in messages)
                    {
                        message.SetEnqueued(null);
                        BacklogEnqueue(message);
                    }
                    StartBacklogProcessor();
                    return true;
                }
                return false;
            }

            var physical = this.physical;
            if (physical == null)
            {
                if (Multiplexer.RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    foreach (var message in messages)
                    {
                        message.SetEnqueued(null);
                        BacklogEnqueue(message);
                    }
                    StartBacklogProcessor();
                    return true;
                }
                return false;
            }
            foreach (var message in messages)
            {
                if (NeedsReconnect || physical.IsWriteFaulted)
                {
                    // the connection went away part-way through the batch (typically an earlier message's write
                    // faulted it); give the remainder the treatment a single command gets at the top of
                    // TryWriteSync - backlog or fail, per policy - rather than writing to a dead connection (#3282).
                    // Both flags matter: the reconnect flag is cleared as soon as a replacement connection starts
                    // (TryConnect, on another thread), but the connection we are still holding stays poisoned
                    QueueOrFailMessage(message);
                    continue;
                }

                // deliberately not taking a single lock here; we don't care if
                // other threads manage to interleave - in fact, it would be desirable
                // (to avoid a batch monopolising the connection)
#pragma warning disable CS0618 // Type or member is obsolete
                WriteMessageTakingWriteLockSync(physical, message);
#pragma warning restore CS0618
                LogNonPreferred(message.Flags, isReplica);
            }
            return true;
        }

        private Message? _activeMessage;

        private WriteResult WriteMessageInsideLock(PhysicalConnection physical, Message message)
        {
            if (ConnectionType == ConnectionType.Interactive
                && message.IsForSubscriptionBridge
                && Protocol is RedisProtocol.Resp2
                && ServerEndPoint.TryRerouteToSubscriptionBridge(message, this))
            {
                // this was queued while we expected RESP3 - where subscriptions can share the interactive
                // connection (SharedSubscriptionConnection) - but the handshake resolved to RESP2, which needs them on their own connection.
                // Writing it here would put *this* connection into subscriber mode, which rejects every
                // ordinary command on it from then on; see #3154
                Trace($"Rerouting {message.CommandAndKey} to the subscription connection (RESP2)");
                return WriteResult.Success;
            }

            WriteResult result;
            var existingMessage = Interlocked.CompareExchange(ref _activeMessage, message, null);
            if (existingMessage != null)
            {
                Multiplexer.OnInfoMessage($"Reentrant call to WriteMessageTakingWriteLock for {message.CommandAndKey}, {existingMessage.CommandAndKey} is still active");
                return WriteResult.NoConnectionAvailable;
            }

            physical.SetWriting();
            if (message is IMultiMessage multiMessage)
            {
                var messageIsSent = false;
                SelectDatabaseInsideWriteLock(physical, message); // need to switch database *before* the transaction
                foreach (var subCommand in multiMessage.GetMessages(physical))
                {
                    // sub-commands do not pass through SetEnqueued (only the parent did, before the backlog);
                    // without a write time the heartbeat sees them as overdue by the process uptime (#3292)
                    if (!ReferenceEquals(subCommand, message)) subCommand.InheritWriteTime(message);
                    result = WriteMessageToServerInsideWriteLock(physical, subCommand);
                    if (result != WriteResult.Success)
                    {
                        // we screwed up; abort; note that WriteMessageToServer already
                        // killed the underlying connection
                        Trace("Unable to write to server");
                        message.Fail(ConnectionFailureType.ProtocolFailure, null, "failure before write: " + result.ToString(), Multiplexer);
                        message.Complete(physical);
                        return result;
                    }
                    // The parent message (next) may be returned from GetMessages
                    // and should not be marked as sent again below.
                    messageIsSent = messageIsSent || subCommand == message;
                }
                if (!messageIsSent)
                {
                    message.SetRequestSent(); // well, it was attempted, at least...
                }

                return WriteResult.Success;
            }
            else
            {
                return WriteMessageToServerInsideWriteLock(physical, message);
            }
        }

        [Obsolete("prefer async")]
        internal WriteResult WriteMessageTakingWriteLockSync(PhysicalConnection physical, Message message)
        {
            Trace("Writing: " + message);
            message.SetEnqueued(physical); // this also records the read/write stats at this point

            // AVOID REORDERING MESSAGES
            // Prefer to add it to the backlog if this thread can see that there might already be a message backlog.
            // We do this before attempting to take the write lock, because we won't actually write, we'll just let the backlog get processed in due course
            if (TryPushToBacklog(message, onlyIfExists: true))
            {
                return WriteResult.Success; // queued counts as success
            }

            bool gotLock = false;
            try
            {
                gotLock = _singleWriter.TryTakeInstant();
                if (!gotLock)
                {
                    // If we can't get it *instantaneously*, pass it to the backlog for throughput
                    if (TryPushToBacklog(message, onlyIfExists: false))
                    {
                        return WriteResult.Success; // queued counts as success
                    }

                    // no backlog... try to wait with the timeout;
                    // if we *still* can't get it: that counts as
                    // an actual timeout
                    gotLock = _singleWriter.TryTakeSync();
                    if (!gotLock) return TimedOutBeforeWrite(message);
                }

                if (TryRequeueForHandoff(physical, message, bypassBacklog: false, out var requeued)) return requeued;
                var result = WriteMessageInsideLock(physical, message);

                if ((result == WriteResult.Success) & message.IsFlushRequiredSync)
                {
                    physical.Flush();
                }

                physical.SetIdle();
                return result;
            }
            catch (Exception ex) { return HandleWriteException(physical, message, ex); }
            finally
            {
                UnmarkActiveMessage(message);
                if (gotLock)
                {
                    _singleWriter.Release();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryPushToBacklog(Message message, bool onlyIfExists, bool bypassBacklog = false)
        {
            // In the handshake case: send the command directly through.
            // If we're disconnected *in the middle of a handshake*, we've bombed a brand new socket and failing,
            // backing off, and retrying next heartbeat is best anyway.
            // Internal calls also shouldn't queue - try immediately. If these aren't errors (most aren't), we
            // won't alert the user.
            if (bypassBacklog || message.IsInternalCall)
            {
                return false;
            }

            // Note, for deciding emptiness for whether to push onlyIfExists, and start worker,
            // we only need care if WE are able to see the queue when its empty.
            // Not whether anyone else sees it as empty.
            // So strong synchronization is not required.
            if (onlyIfExists && Volatile.Read(ref _backlogCurrentEnqueued) == 0)
            {
                return false;
            }

            BacklogEnqueue(message);

            // The correct way to decide to start backlog process is not based on previously empty
            // but based on a) not empty now (we enqueued!) and b) no backlog processor already running.
            // Which StartBacklogProcessor will check.
            StartBacklogProcessor();
            return true;
        }

        /// <summary>
        /// Takes over a message that was queued against a different bridge of the same endpoint, because the
        /// protocol resolved differently to what we expected when it was queued (see #3154).
        /// </summary>
        internal void AcceptRerouted(Message message)
        {
            BacklogEnqueue(message);
            StartBacklogProcessor();
        }

        /// <summary>
        /// The one place a message enters the backlog, and so the one place the hold's cover is applied: whatever
        /// enters during a handoff is held - diverted off the live connection, queued while the bridge reconnects to
        /// the replacement, or parked behind held work being flushed - and gets the same cover, to the same deadline.
        /// The shared deadline also keeps the timeout sweeps' head-of-line rule sound for everything enqueued fresh: a
        /// message enqueued later does not time out before one enqueued earlier, because an earlier held message
        /// expires at the same deadline or sooner, and an ordinary message can only be enqueued after the handoff is
        /// done, which is when nothing held is left or that deadline has passed. The one exception is a message
        /// requeued by a writer that reached the lock after the retire: it keeps its older write time (the budget it
        /// spent counts) behind heads with newer ones, so where its own timeout rather than the shared deadline is the
        /// later of the two it can be reported late - by at most the heads' remaining budget - never early, and never
        /// lost.
        /// <para>
        /// Internal traffic (keep-alives, probes) is not held: a stale probe has nothing to gain from lingering to the
        /// handoff's deadline, and it is never diverted in the first place. An internal message behind held heads is
        /// at worst reported late, which costs nothing.
        /// </para>
        /// </summary>
        /// <param name="message">The message to queue.</param>
        /// <param name="diverted">The message was diverted by a handoff's hold (or requeued after the retire): held
        /// whatever the phase is by now, since a hold that resolved between the gate and the enqueue does not make it
        /// ordinary work.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BacklogEnqueue(Message message, bool diverted = false)
        {
            // the same handoff decides the gate and the stamp: a message gated on one handoff must not carry the
            // deadline of the one before it (a diverted message after the hold resolved takes the last handoff's)
            var live = TryGetHandoff(out var handoff);
            var holdable = !message.IsInternalCall;
            if ((diverted || live) && holdable && (handoff ?? _handoff) is { } stamp) message.MarkHeldForHandoff(stamp.Deadline.Ticks);
            bool wasEmpty = !BacklogHasItems;
            // important that this *precedes* enqueue, to play well with HasPendingCallerFacingItems
            Interlocked.Increment(ref _backlogCurrentEnqueued);
            Interlocked.Increment(ref _backlogTotalEnqueued);
            _backlog.Enqueue(message);
            message.SetBacklogged();
            if (holdable && !live)
            {
                // enqueue, fence, re-read: a hold that began between the read above and the enqueue has walked the
                // queue without this message, or is about to walk it with it; either way it ends up stamped
                Interlocked.MemoryBarrier();
                if (TryGetHandoff(out var late)) message.MarkHeldForHandoff(late.Deadline.Ticks);
            }

            if (wasEmpty)
            {
                // it is important to do this *after* adding, so that we can't
                // get into a thread-race where the heartbeat checks too fast;
                // the fact that we're accessing Multiplexer down here means that
                // we're rooting it ourselves via the stack, so we don't need
                // to worry about it being collected until at least after this
                Multiplexer.Root();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool BacklogTryDequeue([NotNullWhen(true)] out Message? message)
        {
            if (_backlog.TryDequeue(out message))
            {
                Interlocked.Decrement(ref _backlogCurrentEnqueued);
                return true;
            }
            return false;
        }

        /// <summary>Whether the backlog holds a caller's message; snapshot enumeration, which a concurrent queue allows.</summary>
        private bool AnyCallerFacingQueued()
        {
            foreach (var message in _backlog)
            {
                if (message.IsCallerFacing) return true;
            }
            return false;
        }

        /// <summary>
        /// Queues work held for a maintenance handoff - diverted at the gate, or requeued by a writer that reached the
        /// lock to find its connection retired - and, through <see cref="WakeIfHoldReleased"/>, makes sure it is not
        /// stranded if the hold is released concurrently: the queue is stored, then a full fence, then the phase is
        /// loaded - the mirror image of <see cref="OnReplacementEstablished"/>, which stores the phase, fences, then
        /// loads the queue. With a full fence on both sides at least one of them sees the other's store, so one side or
        /// the other starts the processor; neither starts one for an empty backlog.
        /// </summary>
        /// <remarks>
        /// A requeued message is appended, not inserted at the head: a head insert would buy only a best effort at the
        /// order the writer would have had (a writer starved past the start of the flush lands behind flushed work
        /// anyway), at the price of a second queue and a head-mutation discipline across every backlog operation. The
        /// write path never promised more than this in the same races.
        /// </remarks>
        /// <param name="message">The message to queue.</param>
        /// <param name="requeue">The message had already been enqueued (to the connection that has since been retired):
        /// it keeps its write time, since the budget it has spent still counts, and only forgets that connection.</param>
        private void EnqueueHeldAndWakeIfReleased(Message message, bool requeue = false)
        {
            if (requeue) message.SetRequeued();
            else message.SetEnqueued(null);
            BacklogEnqueue(message, diverted: true);
            WakeIfHoldReleased();
        }

        /// <summary>A batch held as a unit: every member queued, then one fence and one check for the batch.</summary>
        private void EnqueueHeldBatchAndWakeIfReleased(List<Message> messages)
        {
            foreach (var message in messages)
            {
                message.SetEnqueued(null);
                BacklogEnqueue(message, diverted: true);
            }
            WakeIfHoldReleased();
        }

        /// <summary>
        /// The enqueue side of the pairing with <see cref="OnReplacementEstablished"/>, and the only one: the queue
        /// has been stored (by whichever helper called this), then a full fence here, then the phase is loaded.
        /// </summary>
        private void WakeIfHoldReleased()
        {
            Interlocked.MemoryBarrier();
            if (!HoldUnresolved) StartBacklogProcessor();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StartBacklogProcessor()
        {
            if (Interlocked.CompareExchange(ref _backlogProcessorIsRunning, 1, 0) == 0)
            {
                var successfullyStarted = false;
                try
                {
                    _backlogStatus = BacklogStatus.Activating;

                    // Start the backlog processor; this is a bit unorthodox, as you would *expect* this to just
                    // be Task.Run; that would work fine when healthy, but when we're falling on our face, it is
                    // easy to get into a thread-pool-starvation "spiral of death" if we rely on the thread-pool
                    // to unblock the thread-pool when there could be sync-over-async callers. Note that in reality,
                    // the initial "enough" of the back-log processor is typically sync, which means that the thread
                    // we start is actually useful, despite thinking "but that will just go async and back to the pool"
                    var thread = new Thread(s => ((PhysicalBridge)s!).ProcessBacklog())
                    {
                        IsBackground = true,                  // don't keep process alive (also: act like the thread-pool used to)
                        Name = "StackExchange.Redis Backlog", // help anyone looking at thread-dumps
                    };

                    thread.Start(this);
                    successfullyStarted = true;
                }
                catch (Exception ex)
                {
                    OnInternalError(ex);
                    Trace("StartBacklogProcessor failed to start backlog processor thread: " + ex.Message);
                }
                finally
                {
                    // If thread failed to start - reset flag to ensure next call doesn't erroneously think backlog process is running
                    if (!successfullyStarted)
                    {
                        _backlogStatus = BacklogStatus.Inactive;
                        Interlocked.Exchange(ref _backlogProcessorIsRunning, 0);
                    }
                }
            }
            else
            {
                try
                {
                    _backlogAutoReset.Set();
                }
                catch (ObjectDisposedException)
                {
                    // disposed between the check and the wake; the processor that was running is winding down anyway
                }
            }
        }

        /// <summary>
        /// Whether this bridge owes a *caller* anything, ignoring our own internal traffic.
        /// </summary>
        internal bool HasCallerWork()
        {
            if (physical?.HasCallerMessagesAwaitingResponse() == true) return true;

            return AnyCallerFacingQueued();
        }

        /// <summary>
        /// Crawls from the head of the backlog queue, consuming anything that should have timed out
        /// and pruning it accordingly (these messages will get timeout exceptions).
        /// </summary>
        private void CheckBacklogForTimeouts()
        {
            var now = Environment.TickCount;

            // deliberately *not* _singleWriter.TimeoutMilliseconds, which is the write-lock acquisition
            // timeout: that is about contention between writers, and relaxing it during maintenance is not
            // something anybody asked for. This is the age at which a queued command is considered timed out,
            // relaxed while the server has announced a disruption.
            var timeout = ServerEndPoint.GetEffectiveTimeoutMilliseconds(Multiplexer.TimeoutMilliseconds);

            // Because peeking at the backlog, checking message and then dequeuing, is not thread-safe, we do have to use
            // a lock here, for mutual exclusion of backlog DEQUEUERS. Unfortunately.
            // But we reduce contention by only locking if we see something that looks timed out.
            // a command a maintenance handoff held is covered until the handoff's deadline; an ordinary command keeps
            // the timeout above. The head-of-line rule below still holds across the two: see BacklogEnqueue
            static int TimeoutFor(Message m, int timeout) => m.HeldForHandoff ? ServerEndPoint.GetEffectiveTimeoutMilliseconds(timeout, m) : timeout;

            while (_backlog.TryPeek(out Message? message))
            {
                // See if the message has pass our async timeout threshold
                // Note: All timed out messages must be dequeued, even when no completion is needed, to be able to dequeue and complete other timed out messages.
                if (!message.HasTimedOut(now, TimeoutFor(message, timeout), out var _)) break; // not a timeout - we can stop looking
                lock (_backlog)
                {
                    // Peek again since we didn't have lock before...
                    // and rerun the exact same checks as above, note that it may be a different message now
                    if (!_backlog.TryPeek(out message)) break;
                    if (!message.HasTimedOut(now, TimeoutFor(message, timeout), out var _)) break;

                    if (!BacklogTryDequeue(out var message2) || (message != message2)) // consume it for real
                    {
                        throw new RedisException("Thread safety bug detected! A queue message disappeared while we had the backlog lock");
                    }
                }

                // We only handle async timeouts here, synchronous timeouts are handled upstream.
                // Those sync timeouts happen in ConnectionMultiplexer.ExecuteSyncImpl() via Monitor.Wait.
                if (message.ResultBoxIsAsync)
                {
                    // Tell the message it has failed
                    // Note: Attempting to *avoid* reentrancy/deadlock issues by not holding the lock while completing messages.
                    var ex = Multiplexer.GetException(WriteResult.TimeoutBeforeWrite, message, ServerEndPoint, this);
                    message.SetExceptionAndComplete(ex, null);
                }
            }
        }

        internal enum BacklogStatus : byte
        {
            Inactive,
            Activating,
            Starting,
            Started,
            CheckingForWork,
            SpinningDown,
            CheckingForTimeout,
            CheckingForTimeoutComplete,
            RecordingTimeout,
            WritingMessage,
            Flushing,
            MarkingInactive,
            RecordingWriteFailure,
            RecordingFault,
            SettingIdle,
            Faulted,
            NotifyingDisposed,
        }

        private volatile BacklogStatus _backlogStatus;

        /// <summary>
        /// Process the backlog(s) in play if any.
        /// This means flushing commands to an available/active connection (if any) or spinning until timeout if not.
        /// </summary>
        private void ProcessBacklog()
        {
            _backlogStatus = BacklogStatus.Starting;
            try
            {
                while (!isDisposed)
                {
                    if (BacklogHasItems)
                    {
                        // TODO: vNext handoff this backlog to another primary ("can handle everything") connection
                        // and remove any per-server commands. This means we need to track a bit of whether something
                        // was server-endpoint-specific in PrepareToPushMessageToBridge (was the server ref null or not)
                        ProcessBridgeBacklog();
                    }

                    // The cost of starting a new thread is high, and we can bounce in and out of the backlog a lot.
                    // So instead of just exiting, keep this thread waiting for 5 seconds to see if we got another backlog item.
                    _backlogStatus = BacklogStatus.SpinningDown;
                    // Note this is happening *outside* the lock
                    var gotMore = _backlogAutoReset.WaitOne(5000);
                    if (!gotMore)
                    {
                        break;
                    }
                }
                // If we're being disposed but have items in the backlog, we need to complete them or async messages can linger forever.
                if (isDisposed && BacklogHasItems)
                {
                    _backlogStatus = BacklogStatus.NotifyingDisposed;
                    // Because peeking at the backlog, checking message and then dequeuing, is not thread-safe, we do have to use
                    // a lock here, for mutual exclusion of backlog DEQUEUERS. Unfortunately.
                    // But we reduce contention by only locking if we see something that looks timed out.
                    while (BacklogHasItems)
                    {
                        Message? message = null;
                        lock (_backlog)
                        {
                            if (!BacklogTryDequeue(out message))
                            {
                                break;
                            }
                        }

                        var ex = ExceptionFactory.Timeout(Multiplexer, "The message was in the backlog when connection was disposed", message, ServerEndPoint, WriteResult.TimeoutBeforeWrite, this);
                        message.SetExceptionAndComplete(ex, null);
                    }
                }
            }
            catch (ObjectDisposedException) when (!BacklogHasItems)
            {
                // We're being torn down and we have no backlog to process - all good.
            }
            catch (Exception)
            {
                _backlogStatus = BacklogStatus.Faulted;
            }
            finally
            {
                // Do this in finally block, so that thread aborts can't convince us the backlog processor is running forever
                if (Interlocked.CompareExchange(ref _backlogProcessorIsRunning, 0, 1) != 1)
                {
                    throw new RedisException("Bug detection, couldn't indicate shutdown of backlog processor");
                }

                // Now that nobody is processing the backlog, we should consider starting a new backlog processor
                // in case a new message came in after we ended this loop.
                if (BacklogHasItems)
                {
                    // Check for faults mainly to prevent unlimited tasks spawning in a fault scenario
                    // This won't cause a StackOverflowException due to the Task.Run() handoff
                    if (_backlogStatus != BacklogStatus.Faulted)
                    {
                        StartBacklogProcessor();
                    }
                }
            }
        }

        /// <summary>
        /// Reset event for monitoring backlog additions mid-run.
        /// This allows us to keep the thread around for a full flush and prevent "feathering the throttle" trying
        /// to flush it. In short, we don't start and stop so many threads with a bit of linger.
        /// </summary>
        private readonly AutoResetEvent _backlogAutoReset = new AutoResetEvent(false);

        private void ProcessBridgeBacklog()
        {
            // Importantly: don't assume we have a physical connection here
            // We are very likely to hit a state where it's not re-established or even referenced here
            bool gotLock = false;
            _backlogAutoReset.Reset();
            try
            {
                _backlogStatus = BacklogStatus.Starting;

                // First eliminate any messages that have timed out already.
                _backlogStatus = BacklogStatus.CheckingForTimeout;
                CheckBacklogForTimeouts();
                _backlogStatus = BacklogStatus.CheckingForTimeoutComplete;

                // For the rest of the backlog, if we're not connected there's no point - abort out
                while (IsConnected)
                {
                    // check whether the backlog is empty *before* even trying to get the lock
                    if (!BacklogHasItems) return; // nothing to do

                    // a maintenance handoff is holding work back from the connection about to be retired: nothing would
                    // be written, and the lock is better left to the drain and the writers it is waiting on; the
                    // release kicks this again
                    if (HoldUnresolved) return;

                    // try and get the lock; if unsuccessful, retry
                    gotLock = _singleWriter.TryTakeSync();
                    if (gotLock) break; // got the lock; now go do something with it
                }
                _backlogStatus = BacklogStatus.Started;

                // Only execute if we're connected.
                // Timeouts are handled above, so we're exclusively into backlog items eligible to write at this point.
                // If we can't write them, abort and wait for the next heartbeat or activation to try this again.
                bool flush = false;
                while (IsConnected && !HoldUnresolved) // not while a handoff is holding work back from the connection about to be retired
                {
                    // Snapshot the connection for the whole of this message: OnDisconnected nulls the field
                    // without waiting for the write lock, so re-reading it (as the loop guard used to) can hand
                    // null to a write path that requires a connection. See #3167.
                    if (this.physical is not { HasOutputPipe: true, RetiredForHandoff: false } physical) break; // retired by a handoff: its teardown follows the lock release

                    Message? message;
                    _backlogStatus = BacklogStatus.CheckingForWork;

                    lock (_backlog)
                    {
                        // Note that we're actively taking it off the queue here, not peeking
                        // If there's nothing left in queue, we're done.
                        if (!BacklogTryDequeue(out message))
                        {
                            break;
                        }
                        _backlogInHand = message; // still "queued" as far as a handoff's flush is concerned, until written
                    }

                    try
                    {
                        _backlogStatus = BacklogStatus.WritingMessage;
                        var result = WriteMessageInsideLock(physical, message);

                        // use flush logic that assumes a sync caller
                        if (!flush) flush = message.IsFlushRequiredSync;

                        _backlogStatus = BacklogStatus.MarkingInactive;
                        if (result != WriteResult.Success)
                        {
                            _backlogStatus = BacklogStatus.RecordingWriteFailure;
                            var ex = Multiplexer.GetException(result, message, ServerEndPoint);
                            HandleWriteException(physical, message, ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        _backlogStatus = BacklogStatus.RecordingFault;
                        HandleWriteException(physical, message, ex);
                    }
                    finally
                    {
                        _backlogInHand = null;
                        UnmarkActiveMessage(message);
                    }
                }

                if (flush && IsConnected && physical is { HasOutputPipe: true })
                {
                    // at least one message wants flushing
                    _backlogStatus = BacklogStatus.Flushing;
                    physical?.Flush();
                }

                // the held work has reached the replacement: nothing left to protect (rule a)
                if (_handoff is { } handoff) TryFinishFlush(handoff);

                _backlogStatus = BacklogStatus.SettingIdle;
                physical?.SetIdle();
                _backlogStatus = BacklogStatus.Inactive;
            }
            finally
            {
                if (gotLock)
                {
                    _singleWriter.Release();
                }
            }
        }

        /// <summary>
        /// Flush anything earlier writes left staged, but only if that can be done without waiting.
        /// </summary>
        /// <remarks>
        /// Called from a push transport's batch-end callback, which runs on a transport thread that must
        /// not block. The write lock is what makes staging exclusive - a flush that raced a stage could
        /// hand a half-written frame to a transport mid-<c>Advance</c> - so this either gets the lock
        /// instantly or does nothing, leaving the flush to whoever does hold it.
        /// </remarks>
        internal void TryFlushStagedWrites(PhysicalConnection physical)
        {
            if (!_singleWriter.TryTakeInstant()) return;
            try
            {
                if (physical is { HasOutputPipe: true })
                {
                    physical.Flush();
                }
            }
            catch
            {
                // opportunistic: a flush failure here means the transport is going away, and that is
                // reported through the receiver's OnClosed
            }
            finally
            {
                _singleWriter.Release();
            }
        }

        public bool HasPendingCallerFacingItems()
        {
            if (BacklogHasItems && AnyCallerFacingQueued()) return true;
            return physical?.HasPendingCallerFacingItems() ?? false;
        }

        private WriteResult TimedOutBeforeWrite(Message message)
        {
            // as FailDueToNoConnection: a real exception, not Cancel()
            var ex = ExceptionFactory.Timeout(Multiplexer, null, message, ServerEndPoint, WriteResult.TimeoutBeforeWrite, this);
            Multiplexer.OnMessageFaulted(message, ex);
            message.SetExceptionAndComplete(ex, null);
            return WriteResult.TimeoutBeforeWrite;
        }

        /// <summary>
        /// For a writer that has just taken the write lock: if the connection it was headed for was retired by a
        /// maintenance handoff while it waited, nothing is written to it - its teardown follows the lock release. A
        /// caller's message is queued for the replacement with the rest of the held work (appended, see
        /// <see cref="EnqueueHeldAndWakeIfReleased"/>); internal traffic and a message bypassing the backlog are failed
        /// as having no connection, as they would be on a disconnected bridge. The connection itself says whether a
        /// handoff retired it (<see cref="PhysicalConnection.RetiredForHandoff"/>), so a writer starved across any
        /// number of later handoffs still recognises its own. Only the handoff case: a write that loses its connection
        /// to an ordinary closure is still reported as a closure (#3167).
        /// </summary>
        private bool TryRequeueForHandoff(PhysicalConnection physical, Message message, bool bypassBacklog, out WriteResult result)
        {
            if (!physical.RetiredForHandoff)
            {
                result = default;
                return false;
            }
            if (bypassBacklog || message.IsInternalCall)
            {
                // not queued, but not written to a connection whose teardown is under way either: a keep-alive or a
                // backlog-bypassing message that reached the lock after the retire simply has no connection
                result = FailDueToNoConnection(message);
                return true;
            }
            EnqueueHeldAndWakeIfReleased(message, requeue: true);
            result = WriteResult.Success; // queued counts as success
            return true;
        }

        /// <summary>
        /// This writes a message to the output stream.
        /// </summary>
        /// <param name="physical">The physical connection to write to.</param>
        /// <param name="message">The message to be written.</param>
        /// <param name="bypassBacklog">Whether this message should bypass the backlog, going straight to the pipe or failing.</param>
        internal ValueTask<WriteResult> WriteMessageTakingWriteLockAsync(PhysicalConnection physical, Message message, bool bypassBacklog = false)
        {
            Trace("Writing: " + message);
            message.SetEnqueued(physical); // this also records the read/write stats at this point

            // AVOID REORDERING MESSAGES
            // Prefer to add it to the backlog if this thread can see that there might already be a message backlog.
            // We do this before attempting to take the write lock, because we won't actually write, we'll just let the backlog get processed in due course
            if (TryPushToBacklog(message, onlyIfExists: true, bypassBacklog: bypassBacklog))
            {
                return new ValueTask<WriteResult>(WriteResult.Success); // queued counts as success
            }

            bool releaseLock = true; // fine to default to true, as it doesn't matter until token is a "success"
            bool gotLock = false;
            try
            {
                // try to acquire it synchronously
                gotLock = _singleWriter.TryTakeInstant();
                if (!gotLock)
                {
                    // If we can't get it *instantaneously*, pass it to the backlog for throughput
                    if (TryPushToBacklog(message, onlyIfExists: false, bypassBacklog: bypassBacklog))
                    {
                        return new ValueTask<WriteResult>(WriteResult.Success); // queued counts as success
                    }

                    // no backlog... try to wait with the timeout;
                    // if we *still* can't get it: that counts as
                    // an actual timeout
                    var pending = _singleWriter.TryTakeAsync();
                    if (!pending.IsCompletedSuccessfully) return WriteMessageTakingWriteLockAsync_Awaited(pending, physical, message, bypassBacklog);

                    gotLock = pending.GetAwaiter().GetResult(); // fine since we know we got a result
                    if (!gotLock) return new ValueTask<WriteResult>(TimedOutBeforeWrite(message));
                }
                if (TryRequeueForHandoff(physical, message, bypassBacklog, out var requeued)) return new ValueTask<WriteResult>(requeued);
                var result = WriteMessageInsideLock(physical, message);
                if ((result == WriteResult.Success) & message.IsFlushRequiredAsync)
                {
                    physical.Flush();
                }

                physical.SetIdle();

                return new ValueTask<WriteResult>(result);
            }
            catch (Exception ex)
            {
                return new ValueTask<WriteResult>(HandleWriteException(physical, message, ex));
            }
            finally
            {
                if (gotLock)
                {
                    UnmarkActiveMessage(message);

                    if (releaseLock)
                    {
                        _singleWriter.Release();
                    }
                }
            }
        }

        private async ValueTask<WriteResult> WriteMessageTakingWriteLockAsync_Awaited(
            ValueTask<bool> pending,
            PhysicalConnection physical,
            Message message,
            bool bypassBacklog)
        {
            bool gotLock = false;
            try
            {
                gotLock = await pending.ForAwait();
                if (!gotLock) return TimedOutBeforeWrite(message);
                if (TryRequeueForHandoff(physical, message, bypassBacklog, out var requeued)) return requeued;
                var result = WriteMessageInsideLock(physical, message);

                if (result == WriteResult.Success)
                {
                    physical.Flush();
                }

                physical.SetIdle();

                return result;
            }
            catch (Exception ex)
            {
                return HandleWriteException(physical, message, ex);
            }
            finally
            {
                UnmarkActiveMessage(message);
                if (gotLock)
                {
                    _singleWriter.Release();
                }
            }
        }

        [SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1519:Braces should not be omitted from multi-line child statement", Justification = "Detector is confused with the #ifdefs here")]
        private async ValueTask<WriteResult> CompleteWriteAndReleaseLockAsync(
            ValueTask<WriteResult> flush,
            Message message)
        {
            try
            {
                var result = await flush.ForAwait();
                physical?.SetIdle();
                return result;
            }
            catch (Exception ex)
            {
                return HandleWriteException(physical, message, ex);
            }
            finally
            {
                _singleWriter.Release();
            }
        }

        private WriteResult HandleWriteException(PhysicalConnection? physical, Message message, Exception ex)
        {
            // FIRST, before anything that can allocate - see the note in WriteMessageToServerInsideWriteLock.
            physical?.PoisonWrite();
            MarkNeedsReconnect();

            var failureType = PhysicalConnection.ClassifyWriteFailure(ex, physical);
            var inner = new RedisConnectionException(failureType, message.Flags, "Failed to write", ex);
            message.SetExceptionAndComplete(inner, physical);
            // Tear down the physical connection. A write that throws may have left a partial frame on the
            // wire, and continuing to use the same socket would let the next reply match the wrong message
            // in the response queue. Forcing a reconnect drains the in-flight queue with failures and
            // restores wire-level synchronization.
            Multiplexer.OnBeforeWriteTeardown();
            physical?.RecordConnectionFailed(failureType, inner);
            return WriteResult.WriteFailure;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UnmarkActiveMessage(Message message)
            => Interlocked.CompareExchange(ref _activeMessage, null, message); // remove if it is us

        private State ChangeState(State newState)
        {
            var oldState = (State)Interlocked.Exchange(ref state, (int)newState);
            if (oldState != newState)
            {
                Multiplexer.Trace(ConnectionType + " state changed from " + oldState + " to " + newState);
            }
            return oldState;
        }

        private bool ChangeState(State oldState, State newState)
        {
            bool result = Interlocked.CompareExchange(ref state, (int)newState, (int)oldState) == (int)oldState;
            if (result)
            {
                Multiplexer.Trace(ConnectionType + " state changed from " + oldState + " to " + newState);
            }
            return result;
        }

        public PhysicalConnection? TryConnect(ILogger? log)
        {
            if (state == (int)State.Disconnected)
            {
                try
                {
                    if (!Multiplexer.IsDisposed)
                    {
                        log?.LogInformationConnecting(Name);
                        Multiplexer.Trace("Connecting...", Name);
                        if (ChangeState(State.Disconnected, State.Connecting))
                        {
                            // Clear the reconnect flag as we're starting a new connection
                            Volatile.Write(ref _needsReconnect, false);
                            Interlocked.Increment(ref socketCount);
                            Interlocked.Exchange(ref connectStartTicks, Environment.TickCount);
                            // separate creation and connection for case when connection completes synchronously
                            // in that case PhysicalConnection will call back to PhysicalBridge, and most PhysicalBridge methods assume that physical is not null;
                            physical = new PhysicalConnection(this, Multiplexer.RawConfig.WriteMode);

                            physical.BeginConnectAsync(log).RedisFireAndForget();
                        }
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    log?.LogErrorConnectFailed(ex, Name, ex.Message);
                    Multiplexer.Trace("Connect failed: " + ex.Message, Name);
                    ChangeState(State.Disconnected);
                    OnInternalError(ex);
                    throw;
                }
            }
            return physical;
        }

        private void LogNonPreferred(CommandFlags flags, bool isReplica)
        {
            if ((flags & Message.InternalCallFlag) == 0) // don't log internal-call
            {
                if (isReplica)
                {
                    if (Message.GetPrimaryReplicaFlags(flags) == CommandFlags.PreferMaster)
                        Interlocked.Increment(ref nonPreferredEndpointCount);
                }
                else
                {
                    if (Message.GetPrimaryReplicaFlags(flags) == CommandFlags.PreferReplica)
                        Interlocked.Increment(ref nonPreferredEndpointCount);
                }
            }
        }

        private void OnInternalError(Exception exception, [CallerMemberName] string? origin = null)
        {
            Multiplexer.OnInternalError(exception, ServerEndPoint.EndPoint, ConnectionType, origin);
        }

        /// <summary>
        /// Writes a fire-and-forget command that is injected ahead of a caller's message (SELECT, READONLY, ASKING, PREPARE).
        /// These never pass through <see cref="Message.SetEnqueued"/>, so the write time is stamped here; some of them are
        /// shared static instances, so the stamp can be overwritten by another connection, but only ever with a later time,
        /// which can make a pending prefix look younger than it is - never older (#3292).
        /// </summary>
        private void WritePrefixCommandInsideWriteLock(PhysicalConnection connection, Message prefix)
        {
            prefix.SetWriteTime();
            connection.EnqueueInsideWriteLock(prefix);
            prefix.WriteTo(connection);
            prefix.SetRequestSent();
            IncrementOpCount();
        }

        private void SelectDatabaseInsideWriteLock(PhysicalConnection connection, Message message)
        {
            int db = message.Db;
            if (db >= 0)
            {
                var sel = connection.GetSelectDatabaseCommand(db, message);
                if (sel != null)
                {
                    WritePrefixCommandInsideWriteLock(connection, sel);
                }
            }
        }

        // HIMPORT is connection-local: a field-set must be PREPAREd on the same physical connection before a SET can
        // reference it. We inject that PREPARE lazily here (mirroring SELECT injection): the first SET for a given
        // field-set on a given connection triggers a fire-and-forget PREPARE ahead of it; a reconnect starts with a
        // fresh (empty) connection set and re-prepares transparently. A DISCARD drops the id so the connection's set
        // stays bounded to live field-sets. All of this runs inside the write lock, so ordering PREPARE-before-SET on
        // the wire is guaranteed and the connection's set needs no further synchronization.
        private void PrepareFieldSetInsideWriteLock(PhysicalConnection connection, Message message)
        {
            if (message is HashImportSetMessage set)
            {
                var fieldSet = set.FieldSet;
                if (connection.TryAddPreparedFieldSet(fieldSet.Id))
                {
                    var prepare = fieldSet.CreatePrepareMessage(message.Db);
                    WritePrefixCommandInsideWriteLock(connection, prepare);
                    fieldSet.RegisterServer(ServerEndPoint, message.Db);
                }
            }
            else if (message is HashImportDiscardMessage discard)
            {
                connection.RemovePreparedFieldSet(discard.FieldSetId);
            }
        }

        private WriteResult WriteMessageToServerInsideWriteLock(PhysicalConnection connection, Message message)
        {
            if (message == null)
            {
                return WriteResult.Success; // for some definition of success
            }

            if (connection.IsWriteFaulted)
            {
                // never add to the response queue of a connection whose queue we no longer trust - but, like every
                // other refusal to write, complete the message *here*: a caller writing a list (a batch) does not
                // act on the result, and a message that is neither queued nor completed is awaited forever (#3282)
                message.Fail(ConnectionFailureType.SocketFailure, null, "an earlier write on this connection failed; this command was not written", Multiplexer);
                message.Complete(connection);
                return WriteResult.WriteFailure;
            }

            bool isQueued = false;
            try
            {
                var cmd = message.Command;
                LastCommand = cmd;
                bool isPrimaryOnly = message.IsPrimaryOnly();

                if (isPrimaryOnly && !ServerEndPoint.SupportsPrimaryWrites)
                {
                    throw ExceptionFactory.PrimaryOnly(Multiplexer.RawConfig.IncludeDetailInExceptions, message.Command, message, ServerEndPoint);
                }
                switch (cmd)
                {
                    case RedisCommand.QUIT:
                        connection.RecordQuit();
                        break;
                    case RedisCommand.EXEC:
                        Multiplexer.OnPreTransactionExec(message); // testing purposes, to force certain errors
                        break;
                }

                SelectDatabaseInsideWriteLock(connection, message);

                if (!connection.TransactionActive)
                {
                    // If we are executing AUTH, it means we are still unauthenticated
                    // Setting READONLY before AUTH always fails but we think it succeeded since
                    // we run it as Fire and Forget.
                    if (cmd != RedisCommand.AUTH && cmd != RedisCommand.HELLO)
                    {
                        var readmode = connection.GetReadModeCommand(isPrimaryOnly);
                        if (readmode != null)
                        {
                            WritePrefixCommandInsideWriteLock(connection, readmode);
                        }
                    }
                    if (message.IsAsking)
                    {
                        WritePrefixCommandInsideWriteLock(connection, ReusableAskingCommand);
                    }
                }
                switch (cmd)
                {
                    case RedisCommand.WATCH:
                    case RedisCommand.MULTI:
                        connection.TransactionActive = true;
                        break;
                    case RedisCommand.UNWATCH:
                    case RedisCommand.EXEC:
                    case RedisCommand.DISCARD:
                        connection.TransactionActive = false;
                        break;
                }

                if (_nextHighIntegrityToken is not 0
                    && !connection.TransactionActive // validated in the UNWATCH/EXEC/DISCARD
                    && message.Command is not RedisCommand.AUTH // if auth fails, later commands may also fail; avoid confusion
                    && message.Command is not RedisCommand.HELLO)
                {
                    // note on the Command match above: curiously, .NET 10 and .NET 11 SDKs emit *opposite* errors here
                    // re "CS9336: The pattern is redundant." ("fixing" one "breaks" the other); possibly a fixed bool inversion
                    // in the analyzer? to avoid pain, we'll just use the most obviously correct form

                    // make sure this value exists early to avoid a race condition
                    // if the response comes back super quickly
                    message.WithHighIntegrity(NextHighIntegrityTokenInsideLock());
                    Debug.Assert(message.IsHighIntegrity, "message should be high integrity");
                }
                else
                {
                    Debug.Assert(!message.IsHighIntegrity, "prior high integrity message found during transaction?");
                }
                if (cmd is RedisCommand.HIMPORT) PrepareFieldSetInsideWriteLock(connection, message);
                connection.EnqueueInsideWriteLock(message);
                isQueued = true;
                message.WriteTo(connection);

                if (message.IsHighIntegrity)
                {
                    message.WriteHighIntegrityChecksumRequest(connection);
                    IncrementOpCount();
                }

                message.SetRequestSent();
                IncrementOpCount();

                // Some commands smash our ability to trust the database
                // and some commands demand an immediate flush
                switch (cmd)
                {
                    case RedisCommand.EVAL:
                    case RedisCommand.EVALSHA:
                        if (!ServerEndPoint.GetFeatures().ScriptingDatabaseSafe)
                        {
                            connection.SetUnknownDatabase();
                        }
                        break;
                    case RedisCommand.UNKNOWN:
                    case RedisCommand.DISCARD:
                    case RedisCommand.EXEC:
                        if (ServerEndPoint.SupportsDatabases)
                        {
                            connection.SetUnknownDatabase();
                        }
                        break;
                }
                return WriteResult.Success;
            }
            catch (RedisCommandException ex) when (!isQueued)
            {
                Trace("Write failed: " + ex.Message);
                message.Fail(ConnectionFailureType.InternalFailure, ex, null, Multiplexer);
                message.Complete(connection);

                // This failed without actually writing; we're OK with that... unless there's a transaction
                if (connection?.TransactionActive == true)
                {
                    // We left it in a broken state - need to kill the connection
                    connection.RecordConnectionFailed(ConnectionFailureType.ProtocolFailure, ex);
                    return WriteResult.WriteFailure;
                }
                return WriteResult.Success;
            }
            catch (Exception ex)
            {
                // FIRST, before anything that can allocate: mark the connection untrustworthy. Everything
                // below here allocates, and it only runs because something already failed; under memory
                // exhaustion it can fail again, and then we would never reach RecordConnectionFailed and
                // would leave a queued-but-unwritten message desyncing every later reply (#2919).
                connection?.PoisonWrite();
                MarkNeedsReconnect();

                Trace("Write failed: " + ex.Message);

                // Most likely an IOException, or the connection being torn down underneath us
                var failureType = PhysicalConnection.ClassifyWriteFailure(ex, connection);
                message.Fail(failureType, ex, null, Multiplexer);
                message.Complete(connection);

                // We don't know how far the write got; kill the connection
                Multiplexer.OnBeforeWriteTeardown();
                connection?.RecordConnectionFailed(failureType, ex);
                return WriteResult.WriteFailure;
            }
        }

        private uint NextHighIntegrityTokenInsideLock()
        {
            // inside lock: no concurrency concerns here
            switch (_nextHighIntegrityToken)
            {
                case 0: return 0; // disabled
                case uint.MaxValue:
                    // avoid leaving the value at zero due to wrap-around
                    _nextHighIntegrityToken = 1;
                    return ushort.MaxValue;
                default:
                    return _nextHighIntegrityToken++;
            }
        }

        internal bool CanSimulateConnectionFailure => Multiplexer.RawConfig.AllowAdmin && physical?.CanSimulateConnectionFailure == true;

        /// <summary>
        /// For testing only.
        /// </summary>
        internal void SimulateConnectionFailure(SimulatedFailureType failureType)
        {
            if (!Multiplexer.RawConfig.AllowAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(Multiplexer.RawConfig.IncludeDetailInExceptions, RedisCommand.DEBUG, null, ServerEndPoint); // close enough
            }
            physical?.SimulateConnectionFailure(failureType);
        }

        internal RedisCommand? GetActiveMessage() => Volatile.Read(ref _activeMessage)?.Command;
    }
}
