﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Maintenance;
using Xunit;
using static StackExchange.Redis.Server.RedisServer;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Receiving the notifications: can the client see them at all, and does it make sense of the payload. Nothing
/// here asserts a *reaction* - there isn't one yet, deliberately - only that a push frame the server sends
/// arrives as an event with the right contents, and that a malformed one is dropped without collateral damage.
/// </summary>
public class MaintenanceNotificationTests(ITestOutputHelper log)
{
    private const int DefaultTimeoutMilliseconds = 5000;

    /// <summary>
    /// The notifications are RESP3 push frames, so every test here forces RESP3 rather than running per
    /// protocol: under RESP2 there is nothing to receive, which is covered by the opt-in tests instead.
    /// </summary>
    private static async Task<(InProcessTestServer Server, ConnectionMultiplexer Connection, EventCollector Events)> ConnectAsync(ITestOutputHelper log)
    {
        var server = new InProcessTestServer(log);
        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.MaintenanceNotifications = MaintenanceNotificationMode.Enabled; // must be live, or the test is vacuous

        var conn = await ConnectionMultiplexer.ConnectAsync(config);
        return (server, conn, new EventCollector(conn));
    }

    private sealed class EventCollector
    {
        private readonly ConcurrentQueue<ServerMaintenanceEvent> _events = new();

        public EventCollector(IConnectionMultiplexer conn)
            => conn.ServerMaintenanceEvent += (_, e) => _events.Enqueue(e);

        public int Count => _events.Count;

        public IReadOnlyList<ServerMaintenanceEvent> All => _events.ToArray();

        public async Task<PushMaintenanceEvent> NextAsync(int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            for (int i = 0; i < timeoutMilliseconds / 25; i++)
            {
                if (_events.TryDequeue(out var next)) return Assert.IsType<PushMaintenanceEvent>(next);
                await Task.Delay(25);
            }

            throw new TimeoutException("No maintenance event was received");
        }

        /// <summary>
        /// Deliberately proves a negative, so it has to wait out a window in which one could have arrived.
        /// </summary>
        public async Task AssertNoneAsync(int milliseconds = 250)
        {
            await Task.Delay(milliseconds);
            Assert.Empty(All);
        }
    }

    [Theory]
    [InlineData(MaintenanceNotificationKind.Migrating, MaintenanceNotificationType.Migrating)]
    [InlineData(MaintenanceNotificationKind.Migrated, MaintenanceNotificationType.Migrated)]
    [InlineData(MaintenanceNotificationKind.FailingOver, MaintenanceNotificationType.FailingOver)]
    [InlineData(MaintenanceNotificationKind.FailedOver, MaintenanceNotificationType.FailedOver)]
    public async Task ShardNotificationIsReceived(MaintenanceNotificationKind sent, MaintenanceNotificationType expected)
    {
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var seq = server.NextMaintenanceSequenceId;
            Assert.Equal(1, server.SendShardNotification(null, sent, timeSeconds: 12, shardIds: "[\"shard:1\"]"));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(expected, evt.NotificationType);
            Assert.Equal(seq, evt.SequenceId);
            Assert.Equal(TimeSpan.FromSeconds(12), evt.Time);
            Assert.Equal("[\"shard:1\"]", evt.Payload); // opaque, carried through verbatim
            Assert.Equal(server.DefaultEndPoint, evt.EndPoint);
            Assert.Null(evt.NewEndPoint);
        }
    }

    [Fact]
    public async Task MovingCarriesItsSuccessor()
    {
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var target = new IPEndPoint(IPAddress.Loopback, 7999);
            Assert.Equal(1, server.SendMoving(null, timeSeconds: 15, newEndpoint: target));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(MaintenanceNotificationType.Moving, evt.NotificationType);
            Assert.Equal(TimeSpan.FromSeconds(15), evt.Time);
            Assert.Equal(target, evt.NewEndPoint);

            // and the deadline is projected forward, which is what a consumer actually wants
            Assert.NotNull(evt.StartTimeUtc);
            Assert.Equal(evt.ReceivedTimeUtc.AddSeconds(15), evt.StartTimeUtc);
        }
    }

    [Fact]
    public async Task MovingWithNoAddressIsStillReported()
    {
        // the documented no-address form: a client must handle it whether or not it asked for "none", and the
        // intended handling is to reconnect to what it already has - so the event must arrive, not be dropped
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendMoving(null, timeSeconds: 15, newEndpoint: null));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(MaintenanceNotificationType.Moving, evt.NotificationType);
            Assert.Null(evt.NewEndPoint);
            Assert.Null(evt.Payload);
        }
    }

    [Theory]
    [InlineData("?:7002")] // unknown node
    [InlineData(":7002")] // node does not know its own address
    [InlineData("host-1.example.com:0")] // no port to dial
    public async Task MovingWithAPlaceholderYieldsNoEndpoint(string placeholder)
    {
        // none of these can be dialled, and none of them may be read as "the server that told us" - so the
        // event arrives with the raw text and no endpoint, rather than with a plausible-looking wrong one
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendRawPush(null, "MOVING", "1", "15", placeholder));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Null(evt.NewEndPoint);
            Assert.Equal(placeholder, evt.Payload);
        }
    }

    [Theory]
    [InlineData(MaintenanceNotificationKind.SlotMigrating, MaintenanceNotificationType.SlotMigrating)]
    [InlineData(MaintenanceNotificationKind.SlotMigrated, MaintenanceNotificationType.SlotMigrated)]
    public async Task SlotNotificationIsReceived(MaintenanceNotificationKind sent, MaintenanceNotificationType expected)
    {
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendSlotNotification(null, sent, slots: "123,456,789-1000"));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(expected, evt.NotificationType);
            Assert.Equal("123,456,789-1000", evt.Payload);
            Assert.Null(evt.Time); // these carry no duration
        }
    }

    [Fact]
    public async Task AllDigitSlotListIsNotMistakenForADuration()
    {
        // why the type decides whether a time is expected, rather than the content: a single-slot list is
        // indistinguishable from a duration by inspection
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendSlotNotification(null, MaintenanceNotificationKind.SlotMigrating, slots: "123"));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal("123", evt.Payload);
            Assert.Null(evt.Time);
        }
    }

    [Fact]
    public async Task IntegersMayArriveAsStrings()
    {
        // "accept $ or : for integers" - the contract says so explicitly, and SendRawPush writes bulk strings
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendRawPush(null, "MIGRATING", "42", "7", "[\"shard:2\"]"));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(42, evt.SequenceId);
            Assert.Equal(TimeSpan.FromSeconds(7), evt.Time);
        }
    }

    [Fact]
    public async Task NegativeTimeIsPreserved()
    {
        // a connection that joins mid-window is told what is left of it, which can be negative: that means
        // "act now", so it must not be clamped or rejected
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: -3));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(TimeSpan.FromSeconds(-3), evt.Time);
            Assert.Null(evt.StartTimeUtc); // nothing sensible to project
        }
    }

    [Fact]
    public async Task LowercaseTypeNamesAreRecognized()
    {
        // no specification is careful about casing, so neither are we
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendRawPush(null, "failing_over", "5", "9", "[]"));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(MaintenanceNotificationType.FailingOver, evt.NotificationType);
        }
    }

    [Fact]
    public async Task TrailingElementsAreTolerated()
    {
        // forwards compatibility is a stated requirement: a future field must not cost us the notification
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendRawPush(null, "MIGRATING", "8", "20", "[\"shard:3\"]", "something-new", "and-another"));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(MaintenanceNotificationType.Migrating, evt.NotificationType);
            Assert.Equal(8, evt.SequenceId);
            Assert.Equal(TimeSpan.FromSeconds(20), evt.Time);
            Assert.Equal("[\"shard:3\"]", evt.Payload);
        }
    }

    [Fact]
    public async Task NestedSlotMigrationsAreRead()
    {
        // the shape the shipped clients read: [type, seq, [[source, target, slots], ...]]. We were dropping
        // this until the prior-art cross-check, because the parser rejected non-scalar elements
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.Equal(1, server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
            [
                ("127.0.0.1:7000", "127.0.0.1:7001", "0-99"),
                ("127.0.0.1:7002", "127.0.0.1:7003", "1000,2000-2500"),
            ]));

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(MaintenanceNotificationType.SlotMigrated, evt.NotificationType);
            Assert.Equal(2, evt.SlotMigrations.Count);

            var first = evt.SlotMigrations[0];
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, 7000), first.Source);
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, 7001), first.Target);
            Assert.Equal(new SlotRange(0, 99), Assert.Single(first.Slots));

            var second = evt.SlotMigrations[1];
            Assert.Equal(2, second.Slots.Count);
            Assert.Equal(new SlotRange(1000, 1000), second.Slots[0]);
            Assert.Equal(new SlotRange(2000, 2500), second.Slots[1]);
            Assert.Equal("1000,2000-2500", second.RawSlots);
        }
    }

    [Fact]
    public async Task CapturedEnterpriseFramesAreUnderstood()
    {
        // Captured from a real Redis Cloud QA endpoint (Enterprise 8.6.2, OSS cluster API) during an actual
        // slot migration on 2026-08-27. Byte for byte, except that the node addresses are rewritten into the
        // private range - the lengths are preserved, so the $20 and $18 counts below are still the real ones:
        //
        //   >3\r\n$10\r\nSMIGRATING\r\n:18\r\n$9\r\n8892-8991\r\n
        //   >3\r\n$9\r\nSMIGRATED\r\n:19\r\n*1\r\n*3\r\n$20\r\n10.129.228.140:13486\r\n
        //       $18\r\n10.252.90.18:13486\r\n$9\r\n8892-8991\r\n
        //
        // Things this pins, each of which was an assumption before: the type name arrives as a *bulk* string;
        // the sequence number as a RESP integer rather than a string; neither cluster notification carries a
        // time element; SMIGRATING's slots are a flat string; and SMIGRATED nests an array *of* triplets - one
        // here - rather than a single flat triple. The fake emits this shape, so this test is the real frame.
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            server.SendSlotNotification(null, MaintenanceNotificationKind.SlotMigrating, "8892-8991", sequenceId: 18);

            var migrating = await events.NextAsync();
            log.WriteLine(migrating.RawMessage ?? "(none)");
            Assert.Equal(MaintenanceNotificationType.SlotMigrating, migrating.NotificationType);
            Assert.Equal(18, migrating.SequenceId);
            Assert.Null(migrating.Time); // no time element on the wire
            Assert.Equal("8892-8991", migrating.Payload);

            server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
                [("10.129.228.140:13486", "10.252.90.18:13486", "8892-8991")], sequenceId: 19);

            var migrated = await events.NextAsync();
            log.WriteLine(migrated.RawMessage ?? "(none)");
            Assert.Equal(MaintenanceNotificationType.SlotMigrated, migrated.NotificationType);
            Assert.Equal(19, migrated.SequenceId);
            Assert.Null(migrated.Time);

            var migration = Assert.Single(migrated.SlotMigrations);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("10.129.228.140"), 13486), migration.Source);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("10.252.90.18"), 13486), migration.Target);
            Assert.Equal(new SlotRange(8892, 8991), Assert.Single(migration.Slots));
        }
    }

    [Fact]
    public async Task CapturedMovingFrameIsUnderstood()
    {
        // Captured from the same deployment during a maintenance_mode scenario, byte for byte:
        //
        //   >4\r\n$6\r\nMOVING\r\n:0\r\n:15\r\n_\r\n
        //
        // Four elements: type, sequence *zero*, a 15-second window, and an explicit RESP3 null for the
        // address - so "no replacement given, reconnect the way you connected". The proxy then closed the
        // socket, which is the point of MOVING: you are told to move, and then the connection goes away.
        //
        // The sequence number is zero because this was the first event of that chain, not because MOVING is
        // special - which is the point: zero is a legal value, so dedup tracks "have we seen one" as a separate
        // bit rather than treating a stored zero as unset.
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            server.SendMoving(null, timeSeconds: 15, newEndpoint: null, sequenceId: 0);

            var moving = await events.NextAsync();
            log.WriteLine(moving.RawMessage ?? "(none)");
            Assert.Equal(MaintenanceNotificationType.Moving, moving.NotificationType);
            Assert.Equal(0, moving.SequenceId);
            Assert.Equal(TimeSpan.FromSeconds(15), moving.Time);
            Assert.Null(moving.NewEndPoint); // the null is the documented "use what you already have"
            Assert.Null(moving.Payload);

            // and the window it opened is what covers the reconnect that follows the socket closing
            var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
            Assert.True(endpoint.IsMaintenanceRelaxed, "MOVING should have relaxed timeouts");
        }
    }

    [Theory]
    [InlineData(MaintenanceNotificationKind.Migrating, MaintenanceNotificationKind.Migrated, "27")]
    [InlineData(MaintenanceNotificationKind.FailingOver, MaintenanceNotificationKind.FailedOver, "21")]
    public async Task CapturedShardNotificationsAreUnderstood(
        MaintenanceNotificationKind opening,
        MaintenanceNotificationKind closing,
        string shard)
    {
        // Captured from the same deployment, byte for byte, running the fault injector's maintenance_mode and
        // failover scenarios:
        //
        //   >4\r\n$9\r\nMIGRATING\r\n:0\r\n:2\r\n$6\r\n["27"]\r\n
        //   >3\r\n$8\r\nMIGRATED\r\n:1\r\n$6\r\n["27"]\r\n
        //   >4\r\n$12\r\nFAILING_OVER\r\n:0\r\n:2\r\n$6\r\n["21"]\r\n
        //   >3\r\n$11\r\nFAILED_OVER\r\n:1\r\n$6\r\n["21"]\r\n
        //
        // Both families have the same shape, and it is the shape the parser assumed: the *opening*
        // notification carries a time and the *closing* one has no time element at all - which is why
        // CarriesTime is keyed on the notification type rather than on sniffing whether the third element
        // looks like a number. The shard list is a stringified JSON array of id strings, quotes included, and
        // is carried through opaquely: nothing a client does depends on which shard it was.
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var shards = $"[\"{shard}\"]";
            server.SendShardNotification(null, opening, timeSeconds: 2, shardIds: shards, sequenceId: 0);

            var open = await events.NextAsync();
            log.WriteLine(open.RawMessage ?? "(none)");
            Assert.Equal(TimeSpan.FromSeconds(2), open.Time);
            Assert.Equal(shards, open.Payload);
            Assert.Equal(0, open.SequenceId);

            // note 2 seconds is what the server actually said, and is shorter than the relaxed timeout floor;
            // the window is clamped up to the floor rather than being honoured literally
            var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
            Assert.True(endpoint.IsMaintenanceRelaxed, $"{opening} should have relaxed timeouts");

            server.SendShardNotification(null, closing, timeSeconds: null, shardIds: shards, sequenceId: 1);

            var closed = await events.NextAsync();
            log.WriteLine(closed.RawMessage ?? "(none)");
            Assert.Null(closed.Time); // three elements on the wire: no time to read
            Assert.Equal(shards, closed.Payload);
            Assert.Equal(1, closed.SequenceId);
        }
    }

    [Fact]
    public async Task OneLogicalEventFromEveryNodeRaisesOneEvent()
    {
        // Observed on the real deployment: every node broadcasts a given event, and all the copies carry the
        // same sequence number - the id identifies the event, not the delivery. So a two-node cluster delivers
        // one migration twice. Both deliveries have to be *acted on*, because the timeout relaxation is
        // per-server, but the consumer wants one callback rather than one per proxy.
        var server = new InProcessTestServer(log) { ServerType = ServerType.Cluster };
        GetHost(server.DefaultEndPoint, out var port);
        var second = server.AddEmptyNode(new IPEndPoint(IPAddress.Loopback, port + 1));
        server.Migrate((RedisKey)"elsewhere", second); // owns something, so the client connects to it

        var config = server.GetClientConfig(defaultOnly: true);
        config.Protocol = RedisProtocol.Resp3;
        config.MaintenanceNotifications = MaintenanceNotificationMode.Enabled;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var events = new EventCollector(conn);
        using (server)
        {
            var muxer = (IInternalConnectionMultiplexer)conn;
            Assert.True(
                await Poll.UntilAsync(() =>
                {
                    var endpoints = conn.GetEndPoints();
                    return endpoints.Length == 2 && endpoints.All(ep => muxer.GetServerEndPoint(ep).MaintenanceNotificationsActive);
                }),
                "both nodes should be connected and opted in, or there is only one delivery and this proves nothing");

            // one send, two clients: this is the fan-out, from the server side
            var delivered = server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 2, shardIds: "[\"27\"]", sequenceId: 5);
            Assert.Equal(2, delivered);

            // both servers relax - that is the internal work that has to happen per connection...
            Assert.True(
                await Poll.UntilAsync(() => conn.GetEndPoints().All(ep => muxer.GetServerEndPoint(ep).IsMaintenanceRelaxed)),
                "every node that was told should have relaxed its own timeouts");

            // ...and the consumer sees it once
            Assert.Equal(1, events.Count);
            var evt = await events.NextAsync();
            Assert.Equal(5, evt.SequenceId);
            log.WriteLine($"{events.Count} event(s) from {delivered} deliveries: {evt.RawMessage}");
            Assert.Contains(evt.EndPoint, conn.GetEndPoints()); // whichever told us first
        }
    }

    /// <summary>
    /// The catch-up channel: Redis Enterprise retains the most recent shard-scoped *completion* and replays it
    /// to each connection that opts in, so a client that connects after a disruption still learns it happened.
    /// </summary>
    /// <remarks>
    /// Observed on RS 8.0.22 (2026-08-28), arriving coalesced into the same TCP read as the <c>+OK</c> for the
    /// opt-in, at ~16 ms. These tests key on the *cause* of that rather than the framing: the frame is flushed
    /// the instant the opt-in is processed, and our opt-in is pipelined early, so a retained frame always
    /// arrives while the connection is still handshaking.
    /// </remarks>
    public class Retention(ITestOutputHelper log)
    {
        private static async Task<(InProcessTestServer Server, ConnectionMultiplexer Connection, EventCollector Events)> ConnectAsync(
            ITestOutputHelper log,
            Action<InProcessTestServer> beforeConnect)
        {
            var server = new InProcessTestServer(log);
            beforeConnect(server); // the event happens *before* anybody connects: that is the whole point

            var config = server.GetClientConfig(defaultOnly: true);
            config.Protocol = RedisProtocol.Resp3;
            config.MaintenanceNotifications = MaintenanceNotificationMode.Enabled;

            var conn = await ConnectionMultiplexer.ConnectAsync(config);
            return (server, conn, new EventCollector(conn));
        }

        [Theory]
        [InlineData(MaintenanceNotificationKind.Migrated)]
        [InlineData(MaintenanceNotificationKind.FailedOver)]
        public async Task RetainedCompletionIsReplayedToANewConnection(MaintenanceNotificationKind kind)
        {
            var (server, conn, events) = await ConnectAsync(log, s =>
                s.SendShardNotification(null, kind, timeSeconds: null, shardIds: "[\"27\"]", sequenceId: 5));

            using (server)
            await using (conn)
            {
                // the collector is attached *after* connecting, so this asserts the frame was handled rather
                // than that the event fired - which is why the relaxed window is the observable here
                var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
                Assert.True(endpoint.IsMaintenanceRelaxed, "the replayed completion should have opened the post-event tail");
                Assert.Equal(MaintenanceNotificationTypeFor(kind), endpoint.ActiveMaintenanceType);
                log.WriteLine($"{kind} replayed; relaxed = {endpoint.IsMaintenanceRelaxed}");

                // ...and it must not have disturbed the handshake it arrived in the middle of, which is the
                // other half of what this test is for: a push frame interleaved with our own handshake
                // replies must be dispatched out-of-band rather than matched against one of them
                Assert.True(conn.IsConnected);
                Assert.True(await conn.GetDatabase().PingAsync() >= TimeSpan.Zero);
                GC.KeepAlive(events);
            }
        }

        private static MaintenanceNotificationType MaintenanceNotificationTypeFor(MaintenanceNotificationKind kind) => kind switch
        {
            MaintenanceNotificationKind.Migrated => MaintenanceNotificationType.Migrated,
            MaintenanceNotificationKind.FailedOver => MaintenanceNotificationType.FailedOver,
            _ => MaintenanceNotificationType.None,
        };

        [Theory]
        [InlineData(MaintenanceNotificationKind.Migrating)]
        [InlineData(MaintenanceNotificationKind.FailingOver)]
        [InlineData(MaintenanceNotificationKind.Moving)]
        [InlineData(MaintenanceNotificationKind.SlotMigrating)]
        [InlineData(MaintenanceNotificationKind.SlotMigrated)]
        public async Task StartersAndSlotScopedEventsAreNotRetained(MaintenanceNotificationKind kind)
        {
            // The design property, asserted as a negative: the catch-up channel can only ever say "a
            // disruption ended", never "one is starting". Nothing that demands action is replayed, so a
            // reconnecting client cannot be told to move by a stale frame - which is what makes the MOVING
            // handoff safe from replay by construction rather than by a guard.
            var (server, conn, events) = await ConnectAsync(log, s =>
            {
                if (kind == MaintenanceNotificationKind.SlotMigrated)
                {
                    s.SendSlotMigrations(null, kind, [("127.0.0.1:7000", "127.0.0.1:7001", "50-60")], sequenceId: 5);
                }
                else
                {
                    s.SendShardNotification(null, kind, timeSeconds: 2, shardIds: "[\"27\"]", sequenceId: 5);
                }
            });

            using (server)
            await using (conn)
            {
                await events.AssertNoneAsync();
                var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
                Assert.False(endpoint.IsMaintenanceRelaxed, $"{kind} must not be retained, so nothing should have relaxed");
            }
        }

        [Fact]
        public async Task RetentionReplacesRatherThanAccumulates()
        {
            // "most recent completion", not a queue: a connection sees at most one of these however many
            // events went past, which is why the client side needs no window or ring for the catch-up case
            var (server, conn, events) = await ConnectAsync(log, s =>
            {
                s.SendShardNotification(null, MaintenanceNotificationKind.Migrated, null, "[\"1\"]", sequenceId: 5);
                s.SendShardNotification(null, MaintenanceNotificationKind.FailedOver, null, "[\"2\"]", sequenceId: 6);
            });

            using (server)
            await using (conn)
            {
                var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
                Assert.True(endpoint.IsMaintenanceRelaxed);
                Assert.Equal(MaintenanceNotificationType.FailedOver, endpoint.ActiveMaintenanceType); // the later one
                GC.KeepAlive(events);
            }
        }

        [Fact]
        public async Task RetentionCanBeTurnedOff()
        {
            // not every deployment retains, and a test of the no-retention case should not have to reason
            // about which notification kinds happen to be retained
            var (server, conn, events) = await ConnectAsync(log, s =>
            {
                s.RetainCompletions = false;
                s.SendShardNotification(null, MaintenanceNotificationKind.Migrated, null, "[\"27\"]", sequenceId: 5);
            });

            using (server)
            await using (conn)
            {
                await events.AssertNoneAsync();
                Assert.False(((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint).IsMaintenanceRelaxed);
            }
        }
    }

    [Theory]
    [InlineData(null)] // the measured shape: the close arrives *after* the announced window
    [InlineData(300)] // a less generous proxy: the close beats the window it announced
    public async Task MovingClosesTheConnectionAndWeRecover(int? closeDelayMilliseconds)
    {
        // The defining half of MOVING, which no test covered until now: we are told to move, and then the
        // socket goes away. Measured on RS (2026-08-28): the close landed at +18.4s and +16.6s against a
        // declared 15s window, so the window is a floor with slack rather than a deadline - which is why the
        // default case here lets the fake overshoot, and why nothing below asserts on the window expiring.
        // The short case is the defensive one: a client that treats the announced window as guaranteed fails
        // it. What must hold either way is that the connection comes back and the relaxed window opened by the
        // notification is what covers the reconnect.
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            server.MovingClosesConnection = true;
            server.MovingCloseDelay = closeDelayMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null;
            server.SendMoving(null, timeSeconds: 1, newEndpoint: null, sequenceId: 0);

            var moving = await events.NextAsync();
            Assert.Equal(MaintenanceNotificationType.Moving, moving.NotificationType);

            var endpoint = ((IInternalConnectionMultiplexer)conn).GetServerEndPoint(server.DefaultEndPoint);
            Assert.True(endpoint.IsMaintenanceRelaxed, "the window has to be open before the socket dies");

            // the client must reconnect on its own; nothing here asks it to. Note the probe has to tolerate
            // throwing rather than returning false: a command issued in the window between the socket dying
            // and the reconnect completing raises, and that is the state being polled *out* of
            Assert.True(
                await Poll.UntilAsync(
                    () =>
                    {
                        try
                        {
                            return conn.IsConnected && conn.GetDatabase().Ping() >= TimeSpan.Zero;
                        }
                        catch (Exception ex) when (ex is RedisException or TimeoutException)
                        {
                            return false;
                        }
                    },
                    timeoutMilliseconds: 15_000),
                "the multiplexer should have re-established itself after the close");

            log.WriteLine($"close delay {closeDelayMilliseconds?.ToString() ?? "announced+slack"}: reconnected, relaxed = {endpoint.IsMaintenanceRelaxed}");
        }
    }

    [Fact]
    public async Task MalformedTripletIsSkippedNotFatal()
    {
        // one bad entry must not lose the migrations we could have applied - the same choice go-redis makes
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
            [
                ("127.0.0.1:7000", "127.0.0.1:7001", "not-a-slot-list"), // kept, but with no parsed slots
                ("127.0.0.1:7002", "?", "50-60"), // an unnameable target, kept with a null Target
                ("127.0.0.1:7004", "127.0.0.1:7005", "70"),
            ]);

            var evt = await events.NextAsync();
            Assert.Equal(3, evt.SlotMigrations.Count);
            Assert.Empty(evt.SlotMigrations[0].Slots);
            Assert.Equal("not-a-slot-list", evt.SlotMigrations[0].RawSlots); // raw form survives
            Assert.Null(evt.SlotMigrations[1].Target);
            Assert.Equal(new SlotRange(50, 60), Assert.Single(evt.SlotMigrations[1].Slots));
            Assert.Equal(new SlotRange(70, 70), Assert.Single(evt.SlotMigrations[2].Slots));
        }
    }

    [Fact]
    public async Task MissingSequenceIdStillReportsTheNotification()
    {
        // stricter-than-shipped-clients was the bug here: go-redis length-checks these at two elements and
        // reads no sequence number at all, so dropping a frame for want of one loses a real disruption
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            // two elements, with an unreadable seq - the floor go-redis works to
            Assert.True(server.SendRawPush(null, "MIGRATING", "not-a-number") > 0);

            var evt = await events.NextAsync();
            log.WriteLine(evt.RawMessage ?? "(no message)");
            Assert.Equal(MaintenanceNotificationType.Migrating, evt.NotificationType);
        }
    }

    [Theory]
    [InlineData("NOT_A_REAL_KIND", "1", "5")] // a type we don't know
    [InlineData("NOT_A_REAL_KIND")] // ...and one with nothing else at all
    public async Task MalformedNotificationIsDroppedAndTheConnectionSurvives(params string[] parts)
    {
        // the important half of this feature's safety: an unparseable push frame must be consumed and
        // forgotten. If it fell through to the command matcher it would steal the next command's reply, and
        // everything after that would be answering the wrong question
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            Assert.True(server.SendRawPush(null, parts) > 0);
            await events.AssertNoneAsync();

            // the connection is not merely alive but *in sync*: each reply is the answer to its own command
            var db = conn.GetDatabase();
            for (int i = 0; i < 10; i++)
            {
                await db.StringSetAsync($"maint-sync-{i}", i);
            }

            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(i, (int)await db.StringGetAsync($"maint-sync-{i}"));
            }
        }
    }

    [Fact]
    public async Task NotificationsArriveInterleavedWithCommands()
    {
        // a push can land at any point, including between a command and its reply; nothing may be lost or
        // mismatched either way
        var (server, conn, events) = await ConnectAsync(log);
        using (server)
        await using (conn)
        {
            var db = conn.GetDatabase();
            var pending = new List<Task<RedisValue>>();
            for (int i = 0; i < 50; i++)
            {
                await db.StringSetAsync($"maint-interleave-{i}", i);
                pending.Add(db.StringGetAsync($"maint-interleave-{i}"));
                server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: i);
            }

            var values = await Task.WhenAll(pending);
            Assert.Equal(Enumerable.Range(0, 50), values.Select(x => (int)x));

            // and every notification arrived
            for (int i = 0; i < 50; i++)
            {
                await events.NextAsync();
            }

            log.WriteLine($"{values.Length} commands and 50 notifications, all accounted for");
        }
    }
}
