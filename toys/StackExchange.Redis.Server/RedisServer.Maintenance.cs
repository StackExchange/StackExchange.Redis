using System;
using System.Net;
using System.Threading;
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

        private int _maintenanceSequence;
        private int _maintenanceOptIns;

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
            => Send(client, MaintenanceNotificationKind.Moving, timeSeconds, sequenceId, newEndpoint, null);

        /// <summary>
        /// Sends one of the shard-scoped notifications. <paramref name="timeSeconds"/> is a remaining-time
        /// delta and may legitimately be zero or negative for a connection that arrived mid-window.
        /// </summary>
        /// <returns>The number of clients the notification was sent to.</returns>
        public int SendShardNotification(RedisClient client, MaintenanceNotificationKind kind, int timeSeconds, string shardIds = null, int? sequenceId = null)
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
            // Rent at every level: Recycle() recurses, so a Standalone child inside a pooled parent gets
            // handed to the pool it did not come from ("The buffer is not associated with this pool")
            var frame = TypedRedisValue.Rent(3, out var span, RespPrefix.Push);
            span[0] = TypedRedisValue.SimpleString(GetName(kind));
            span[1] = TypedRedisValue.Integer(sequenceId ?? Interlocked.Increment(ref _maintenanceSequence));

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
            return Dispatch(client, frame, requireOptIn: true);
        }

        /// <summary>
        /// Sends an arbitrary push frame to a client, for the cases a well-formed notification cannot express:
        /// an unknown type, a malformed payload, extra trailing elements.
        /// </summary>
        /// <returns>The number of clients the frame was sent to.</returns>
        public int SendRawPush(RedisClient client, params string[] parts)
        {
            var frame = TypedRedisValue.Rent(parts.Length, out var span, RespPrefix.Push);
            for (int i = 0; i < parts.Length; i++)
            {
                span[i] = TypedRedisValue.BulkString(parts[i]);
            }
            return Dispatch(client, frame, requireOptIn: false);
        }

        private int Send(
            RedisClient client,
            MaintenanceNotificationKind kind,
            int? timeSeconds,
            int? sequenceId,
            EndPoint newEndpoint,
            string extra)
        {
            // [type, seqID, ...] - the sequence id is an integer, which is precisely why these frames could
            // not be treated as pub/sub: element 1 is not a channel name
            int count = 2 + (timeSeconds.HasValue ? 1 : 0) + (newEndpoint is not null || kind == MaintenanceNotificationKind.Moving ? 1 : 0) + (extra is not null ? 1 : 0);
            var frame = TypedRedisValue.Rent(count, out var span, RespPrefix.Push);

            int index = 0;
            span[index++] = TypedRedisValue.SimpleString(GetName(kind));
            span[index++] = TypedRedisValue.Integer(sequenceId ?? System.Threading.Interlocked.Increment(ref _maintenanceSequence));
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

            return Dispatch(client, frame, requireOptIn: true);
        }

        private int Dispatch(RedisClient client, in TypedRedisValue frame, bool requireOptIn)
        {
            if (client is not null)
            {
                client.AddOutbound(frame);
                return 1;
            }

            // a real server sends only to connections that asked, so sending to all means all *opted-in*.
            // Counting the sends rather than the clients visited: the Action overload of ForAllClients returns
            // one per client regardless, which would report every connection as a recipient
            var copy = frame;
            return ForAllClients(
                requireOptIn,
                (target, gated) =>
                {
                    if (gated && !target.MaintenanceNotifications) return 0;
                    target.AddOutbound(copy);
                    return 1;
                });
        }
    }
}
