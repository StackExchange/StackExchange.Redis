using System;
using System.Net;
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
