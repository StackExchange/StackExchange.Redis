using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Server
{
    /// <summary>
    /// Sending maintenance notifications. Deliberately on the server itself rather than on a bespoke test
    /// subclass: the opt-in is a normal command any test may use, and injecting a notification is a normal
    /// thing any test may want to do. A server that never sends one is the ordinary case - that is what every
    /// OSS build does - so the interesting behaviour is on the client side either way.
    /// </summary>
    public partial class RedisServer
    {
        /// <summary>
        /// The notification types defined by the maintenance-notification contract. <c>SMOVING</c> and
        /// <c>SFAILING_OVER</c> were proposed upstream but never landed, and are deliberately absent.
        /// </summary>
        public enum MaintenanceNotificationKind
        {
            /// <summary>This endpoint is being replaced; the payload names its successor.</summary>
            Moving,

            /// <summary>A shard is migrating away from this node.</summary>
            Migrating,

            /// <summary>The migration has completed.</summary>
            Migrated,

            /// <summary>This node is failing over.</summary>
            FailingOver,

            /// <summary>The failover has completed.</summary>
            FailedOver,

            /// <summary>Slots are migrating (OSS cluster family).</summary>
            SlotMigrating,

            /// <summary>Slots have migrated (OSS cluster family).</summary>
            SlotMigrated,
        }

        private static string GetName(MaintenanceNotificationKind kind) => kind switch
        {
            MaintenanceNotificationKind.Moving => "MOVING",
            MaintenanceNotificationKind.Migrating => "MIGRATING",
            MaintenanceNotificationKind.Migrated => "MIGRATED",
            MaintenanceNotificationKind.FailingOver => "FAILING_OVER",
            MaintenanceNotificationKind.FailedOver => "FAILED_OVER",
            MaintenanceNotificationKind.SlotMigrating => "SMIGRATING",
            MaintenanceNotificationKind.SlotMigrated => "SMIGRATED",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>
        /// An artificial delay applied before each reply, to make command timeouts happen on demand.
        /// </summary>
        /// <remarks>
        /// This is how "does relaxation actually save a command?" gets tested. The obvious alternative - the
        /// fault injector's <c>network_latency</c> action against a real cluster - turns out to be the wrong
        /// tool: it applies netem to a whole *node's* interface, so it delays the cluster's internal traffic
        /// and its DNS as well as the client's, and 200ms across two of three nodes was enough to take a
        /// working deployment offline. Its <c>duration_seconds</c> did not revert either. A per-connection
        /// delay here is precise, instant, and cannot break anything.
        /// <para>
        /// Applies to *every* reply on the connection, including a handshake, so set it after connecting
        /// unless a slow handshake is what is being tested.
        /// </para>
        /// </remarks>
        public TimeSpan ResponseDelay { get; set; }

        /// <inheritdoc/>
        /// <remarks>
        /// Not an <c>async</c> method: the base signature takes the request by <c>in</c>, which async forbids.
        /// </remarks>
        protected override ValueTask ClientPauseAsync(RedisClient client, in RedisRequest request)
        {
            var delay = ResponseDelay;
            return delay > TimeSpan.Zero ? new ValueTask(Task.Delay(delay)) : default;
        }

        private int _maintenanceSequence;
        private int _maintenanceOptIns;
        private (MaintenanceNotificationKind Kind, long Sequence, string ShardIds)? _retainedCompletion;

        /// <summary>
        /// Whether the server keeps the most recent shard-scoped *completion* and replays it to each
        /// connection that opts in, as Redis Enterprise does.
        /// </summary>
        /// <remarks>
        /// Observed on RS 8.0.22 (2026-08-28), and the boundary is sharp: <c>MIGRATED</c> and
        /// <c>FAILED_OVER</c> are retained; <c>MIGRATING</c>, <c>FAILING_OVER</c>, <c>MOVING</c>,
        /// <c>SMIGRATING</c> and <c>SMIGRATED</c> are not. The characterisation that fits all seven is the
        /// completion of a *shard-scoped* event - the two that carry an affected-shards list - so
        /// <c>SMIGRATED</c> (a slot-range triple) and <c>MOVING</c> (an endpoint) are excluded even though
        /// <c>SMIGRATED</c> is also a completion.
        /// <para>
        /// The consequence is a design property worth stating: the catch-up channel can only ever say "a
        /// disruption ended", never "one is starting". Nothing that demands action is replayed, so a
        /// reconnecting client cannot be told to move by a stale frame.
        /// </para>
        /// <para>
        /// Retained "most recent, replaced not accumulated" - one frame, never a queue - so a connection sees
        /// at most one of these however many events went past.
        /// </para>
        /// </remarks>
        public bool RetainCompletions { get; set; } = true;

        /// <summary>
        /// Whether this notification is one of the two the server retains for replay.
        /// </summary>
        private static bool IsRetainedCompletion(MaintenanceNotificationKind kind)
            => kind is MaintenanceNotificationKind.Migrated or MaintenanceNotificationKind.FailedOver;

        /// <summary>
        /// Replays the retained completion, if any, to a client that has just opted in.
        /// </summary>
        /// <remarks>
        /// Deliberately *after* the <c>+OK</c> (see <see cref="RedisClient.AddOutboundAfterReply"/>), and with
        /// the original sequence id rather than a fresh one: the id identifies the event, and a client using it
        /// for dedup has to be able to recognize a completion it has already seen.
        /// </remarks>
        private void ReplayRetainedCompletion(RedisClient client)
        {
            if (!RetainCompletions || _retainedCompletion is not { } retained) return;

            client.AddOutboundAfterReply(BuildShardNotification(retained.Kind, null, retained.Sequence, null, retained.ShardIds));
            Log($"[{client}] replayed retained {GetName(retained.Kind)} (seq {retained.Sequence})");
        }

        /// <summary>
        /// How many times a client has opted in, across the lifetime of the server. Per-client state goes away
        /// with the client, so this is what a reconnect test can measure.
        /// </summary>
        public int TotalMaintenanceOptIns => Volatile.Read(ref _maintenanceOptIns);

        internal void OnMaintenanceOptIn() => Interlocked.Increment(ref _maintenanceOptIns);

        /// <summary>
        /// Whether <see cref="Migrate(int, EndPoint)"/> announces itself the way a real server does, rather
        /// than only moving the slot in this model.
        /// </summary>
        /// <remarks>
        /// Off by default, because plenty of tests use <c>Migrate</c> purely to arrange a topology and would
        /// not expect a notification to arrive mid-arrangement. Turn it on to exercise the *sequence* a client
        /// really sees - which is the only way to test how the notification-driven path and the unsolicited
        /// <c>SUNSUBSCRIBE</c> path interact, since both fire for the same migration.
        /// </remarks>
        public bool NotifyOnMigrate { get; set; }

        /// <summary>
        /// Announces a slot migration the way a real server does: the shard notifications either side of it,
        /// and an unsolicited <c>sunsubscribe</c> to any client subscribed to a sharded channel that has just
        /// moved away.
        /// </summary>
        /// <remarks>
        /// Note the two signals are independent. The notifications only reach clients that opted in, whereas
        /// the unsubscribe is ordinary cluster behaviour and reaches every subscriber - so a client can
        /// legitimately see one, the other, or both, and the order is a server implementation detail. That is
        /// exactly the interaction worth being able to reproduce here.
        /// </remarks>
        private void AnnounceMigration(int hashSlot, Node from, Node to)
        {
            var slots = hashSlot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SendSlotNotification(null, MaintenanceNotificationKind.SlotMigrating, slots);

            var dropped = ForAllClients(hashSlot, static (client, slot) => client.UnsubscribeMigratedSlot(slot));
            if (dropped != 0) Log($"unsubscribed {dropped} sharded subscription(s) for migrated slot {hashSlot}");

            SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
                [($"{from.Host}:{from.Port}", $"{to.Host}:{to.Port}", slots)]);
        }

        /// <summary>
        /// The sequence id given to the next notification, unless one is supplied explicitly. The contract does
        /// not define these, so a client's use of them is its own invention - which is worth being able to
        /// exercise, including by repeating one.
        /// </summary>
        public int NextMaintenanceSequenceId => _maintenanceSequence + 1;

        /// <summary>
        /// Sends <c>MOVING</c> to one client, or to every client that opted in when <paramref name="client"/>
        /// is null. A null <paramref name="newEndpoint"/> is the documented no-address form, which a client
        /// must handle whether or not it asked for <c>none</c>.
        /// </summary>
        /// <returns>The number of clients the notification was sent to.</returns>
        public int SendMoving(RedisClient client, int timeSeconds, EndPoint newEndpoint, int? sequenceId = null)
        {
            var recipients = MovingClosesConnection ? new List<RedisClient>() : null;
            Action<RedisClient> onSent = recipients is null ? null : recipients.Add;
            var count = Send(client, MaintenanceNotificationKind.Moving, timeSeconds, sequenceId, newEndpoint, null, onSent);

            if (recipients is { Count: > 0 })
            {
                // The half of MOVING that defines it: the socket goes away - and it takes every other
                // connection to the same node with it (see MovingClosesConnection).
                var delay = MovingCloseDelay ?? TimeSpan.FromSeconds(Math.Max(timeSeconds, 0)) + MeasuredCloseSlack;
                _ = CloseAfterAsync(CollectSiblings(recipients), delay);
            }

            return count;
        }

        /// <summary>
        /// Whether <see cref="SendMoving"/> then closes the affected connections, as a real proxy does.
        /// </summary>
        /// <remarks>
        /// Note the blast radius is the *node*, not the connection. Measured on RS (2026-08-28) with four
        /// connections to one node differing only in handshake - two opted in, one RESP3 without the opt-in,
        /// one RESP2 - all four closed simultaneously, and only the two that had opted in were warned. So this
        /// is endpoint retirement rather than socket recycling, and a connection that did not opt in gets no
        /// warning at all before it dies: an argument for opting in on every connection rather than one.
        /// </remarks>
        public bool MovingClosesConnection { get; set; }

        /// <summary>
        /// How much later than the announced window the close actually arrives, by default.
        /// </summary>
        /// <remarks>
        /// Measured at +3.4s and +1.6s against a declared 15s grace, i.e. the announced window behaves as a
        /// floor with slack rather than a deadline. Tests should assert that a client acts *within* the window,
        /// never that the socket survives to the end of it.
        /// </remarks>
        public TimeSpan MeasuredCloseSlack { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// How long after <c>MOVING</c> the connections are closed; defaults to the announced window plus
        /// <see cref="MeasuredCloseSlack"/>, which is what a real proxy was measured doing.
        /// </summary>
        /// <remarks>
        /// Set it shorter than the announced window to exercise a proxy less generous than the one measured -
        /// a client that treats the window as guaranteed rather than as a budget fails that case. It cannot
        /// usefully be set to zero: the close would race the delivery of the notification itself, which is not
        /// something any real timing produces.
        /// </remarks>
        public TimeSpan? MovingCloseDelay { get; set; }

        /// <summary>
        /// Expands the notified connections to every connection sharing a node with them.
        /// </summary>
        private List<RedisClient> CollectSiblings(List<RedisClient> notified)
        {
            var nodes = new HashSet<Node>();
            foreach (var client in notified) nodes.Add(client.Node);

            var all = new List<RedisClient>();
            ForAllClients(
                all,
                (client, list) =>
                {
                    if (nodes.Contains(client.Node)) list.Add(client);
                    return 0;
                });
            return all;
        }

        private async Task CloseAfterAsync(List<RedisClient> clients, TimeSpan delay)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay).ConfigureAwait(false);
            foreach (var client in clients)
            {
                Log($"[{client}] closing connection after MOVING");
                client.Kill();
            }
        }

        /// <summary>
        /// Sends one of the shard-scoped notifications. <paramref name="timeSeconds"/> is a remaining-time
        /// delta and may legitimately be zero or negative for a connection that arrived mid-window; pass
        /// <c>null</c> to omit the element entirely, which is what a real server does for the *closing*
        /// notifications - captured from Enterprise 8.6.2:
        /// <code>
        /// &gt;4 $12 FAILING_OVER :0 :2 $6 ["21"]
        /// &gt;3 $11 FAILED_OVER  :1    $6 ["21"]
        /// </code>
        /// </summary>
        /// <returns>The number of clients the notification was sent to.</returns>
        public int SendShardNotification(RedisClient client, MaintenanceNotificationKind kind, int? timeSeconds, string shardIds = null, int? sequenceId = null)
            => Send(client, kind, timeSeconds, sequenceId, null, shardIds);

        /// <summary>
        /// Sends a slot-scoped notification (<c>SMIGRATING</c> / <c>SMIGRATED</c>) carrying a slot list in the
        /// contract's comma-and-range form, e.g. <c>"123,456,789-1000"</c>.
        /// </summary>
        /// <returns>The number of clients the notification was sent to.</returns>
        public int SendSlotNotification(RedisClient client, MaintenanceNotificationKind kind, string slots, int? sequenceId = null)
            => Send(client, kind, null, sequenceId, null, slots);

        /// <summary>
        /// Sends <c>SMIGRATED</c> in its nested form - <c>[type, seq, [[source, target, slots], ...]]</c> -
        /// which is what the shipped clients read (see the topic README's prior art; go-redis reads exactly
        /// this shape and redis-py models the same nesting).
        /// </summary>
        /// <remarks>
        /// Note the sender is not implicitly the source of anything: every node reports the same movements, so
        /// a test can and should exercise a delta that does not involve this server at all.
        /// </remarks>
        /// <returns>The number of clients the notification was sent to.</returns>
        public int SendSlotMigrations(RedisClient client, MaintenanceNotificationKind kind, (string Source, string Target, string Slots)[] migrations, int? sequenceId = null)
        {
            // one sequence id for the notification, however many clients it is delivered to - it identifies the
            // event, not the delivery, which is what a real server does and what client-side dedup relies on
            var seq = sequenceId ?? Interlocked.Increment(ref _maintenanceSequence);
            return Dispatch(client, Build, requireOptIn: true);

            TypedRedisValue Build()
            {
                // Rent at every level: Recycle() recurses, so a Standalone child inside a pooled parent gets
                // handed to the pool it did not come from ("The buffer is not associated with this pool")
                var frame = TypedRedisValue.Rent(3, out var span, RespPrefix.Push);
                // bulk, not simple: that is what a real server sends. Captured from Enterprise 8.6.2:
                //   >3 $9 SMIGRATED :19 *1[ *3[ $20 <source> $18 <target> $9 <slots> ] ]
                span[0] = TypedRedisValue.BulkString(GetName(kind));
                span[1] = TypedRedisValue.Integer(seq);

                var outer = TypedRedisValue.Rent(migrations.Length, out var outerSpan, RespPrefix.Array);
                for (int i = 0; i < migrations.Length; i++)
                {
                    var triplet = TypedRedisValue.Rent(3, out var inner, RespPrefix.Array);
                    inner[0] = TypedRedisValue.BulkString(migrations[i].Source);
                    inner[1] = TypedRedisValue.BulkString(migrations[i].Target);
                    inner[2] = TypedRedisValue.BulkString(migrations[i].Slots);
                    outerSpan[i] = triplet;
                }

                span[2] = outer;
                return frame;
            }
        }

        /// <summary>
        /// Sends an arbitrary push frame to a client, for the cases a well-formed notification cannot express:
        /// an unknown type, a malformed payload, extra trailing elements.
        /// </summary>
        /// <returns>The number of clients the frame was sent to.</returns>
        public int SendRawPush(RedisClient client, params string[] parts)
        {
            return Dispatch(client, Build, requireOptIn: false);

            TypedRedisValue Build()
            {
                var frame = TypedRedisValue.Rent(parts.Length, out var span, RespPrefix.Push);
                for (int i = 0; i < parts.Length; i++)
                {
                    span[i] = TypedRedisValue.BulkString(parts[i]);
                }
                return frame;
            }
        }

        private int Send(
            RedisClient client,
            MaintenanceNotificationKind kind,
            int? timeSeconds,
            int? sequenceId,
            EndPoint newEndpoint,
            string extra,
            Action<RedisClient> onSent = null)
        {
            // [type, seqID, ...] - the sequence id is an integer, which is precisely why these frames could
            // not be treated as pub/sub: element 1 is not a channel name
            var seq = sequenceId ?? System.Threading.Interlocked.Increment(ref _maintenanceSequence);

            // the retention is server-wide and replaces rather than accumulates, as a real one does; note it
            // records what was *sent*, so a test arranges it by sending the completion, not by poking state
            if (IsRetainedCompletion(kind)) _retainedCompletion = (kind, seq, extra);

            return Dispatch(client, Build, requireOptIn: true, onSent);

            TypedRedisValue Build() => BuildShardNotification(kind, timeSeconds, seq, newEndpoint, extra);
        }

        private static TypedRedisValue BuildShardNotification(
            MaintenanceNotificationKind kind,
            int? timeSeconds,
            long seq,
            EndPoint newEndpoint,
            string extra)
        {
            int count = 2 + (timeSeconds.HasValue ? 1 : 0) + (newEndpoint is not null || kind == MaintenanceNotificationKind.Moving ? 1 : 0) + (extra is not null ? 1 : 0);
            var frame = TypedRedisValue.Rent(count, out var span, RespPrefix.Push);

            int index = 0;
            span[index++] = TypedRedisValue.BulkString(GetName(kind)); // bulk, as a real server sends
            span[index++] = TypedRedisValue.Integer(seq);
            if (timeSeconds.HasValue) span[index++] = TypedRedisValue.Integer(timeSeconds.GetValueOrDefault());
            if (kind == MaintenanceNotificationKind.Moving)
            {
                // null rather than absent when there is no address: the client must cope with both
                span[index++] = newEndpoint is null
                    ? TypedRedisValue.BulkString(RedisValue.Null)
                    : TypedRedisValue.BulkString(Format.ToString(newEndpoint));
            }
            else if (newEndpoint is not null)
            {
                span[index++] = TypedRedisValue.BulkString(Format.ToString(newEndpoint));
            }
            if (extra is not null) span[index] = TypedRedisValue.BulkString(extra);

            return frame;
        }

        /// <summary>
        /// Sends a notification to one client or to every opted-in client, building the frame per recipient.
        /// </summary>
        /// <remarks>
        /// The factory is called once per recipient rather than the frame being built once and shared, because
        /// each client's write loop recycles what it wrote: a shared frame is returned to the pool by the first
        /// writer and the second one faults with "Array element cannot be nil", killing that connection. Every
        /// broadcast therefore used to reach exactly one client, which is invisible in a single-node test and
        /// silently made multi-node fan-out untestable.
        /// </remarks>
        private int Dispatch(RedisClient client, Func<TypedRedisValue> frameFactory, bool requireOptIn, Action<RedisClient> onSent = null)
        {
            if (client is not null)
            {
                client.AddOutbound(frameFactory());
                onSent?.Invoke(client);
                return 1;
            }

            // a real server sends only to connections that asked, so sending to all means all *opted-in*.
            // Counting the sends rather than the clients visited: the Action overload of ForAllClients returns
            // one per client regardless, which would report every connection as a recipient
            return ForAllClients(
                requireOptIn,
                (target, gated) =>
                {
                    if (gated && !target.MaintenanceNotifications) return 0;
                    target.AddOutbound(frameFactory());
                    onSent?.Invoke(target);
                    return 1;
                });
        }
    }
}
