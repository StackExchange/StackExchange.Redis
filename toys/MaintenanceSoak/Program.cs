using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using StackExchange.Redis.MaintenanceSoak;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Server;
using static StackExchange.Redis.Server.RedisServer;

// Soak for the maintenance-notification machinery: continuous traffic while notifications are injected on a
// loop, watching for the things that only appear with repetition.
//
// Why this exists, when the feature already has unit tests and a live scenario tier: stage 2 onwards introduced
// real state with lifetimes - relaxed windows that extend, a post-event tail, an eight-slot dedup ring, a
// handoff in-flight flag, a handoff target with an expiry - and its failure modes are "a window never closes",
// "a flag is never cleared", "something accumulates". None of those show up once; all of them show up on the
// thousandth cycle. That is the gap this fills, and it is what discharges NF.1.
//
// Usage: MaintenanceSoak [cycles] [--workers N] [--port N]

int cycles = 500, workers = 8, port = 0;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--workers" && i + 1 < args.Length) workers = int.Parse(args[++i]);
    else if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
    else if (int.TryParse(args[i], out var parsed)) cycles = parsed;
}

Console.WriteLine($"soak: {cycles} cycles, {workers} workers");

var invariants = new Invariants();
await using var host = new SoakServer(new MemoryCacheRedisServer(), port);
Console.WriteLine($"server listening on {host.EndPoint}");

var options = new ConfigurationOptions
{
    EndPoints = { host.EndPoint },
    Protocol = RedisProtocol.Resp3,
    MaintenanceNotifications = MaintenanceNotificationMode.Enabled,
    AbortOnConnectFail = false,
    ConnectTimeout = 5_000,
    SyncTimeout = 5_000,
    AllowAdmin = true,

    // Short windows on purpose. The defaults are a 10s floor with a 20s tail, which is right for a real
    // deployment and useless here: the soak injects continuously, so it would spend its whole life inside one
    // window and never observe one *closing*. One second exercises the same code with a tractable clock.
    MaintenanceRelaxedTimeout = TimeSpan.FromSeconds(1),
};

await using var muxer = await ConnectionMultiplexer.ConnectAsync(options);
var server = ((IInternalConnectionMultiplexer)muxer).GetServerEndPoint(host.EndPoint);
Console.WriteLine($"connected; maintenance notifications active: {server.MaintenanceNotificationsActive}");
if (!server.MaintenanceNotificationsActive)
{
    Console.Error.WriteLine("FAIL: the opt-in was not accepted, so this run would prove nothing");
    return 2;
}

// ---- continuous traffic -------------------------------------------------------------------------------------
using var running = new CancellationTokenSource();
long commands = 0, failures = 0;
var traffic = Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
{
    var db = muxer.GetDatabase();
    var key = (RedisKey)$"soak-{worker}";
    while (!running.IsCancellationRequested)
    {
        try
        {
            await db.StringSetAsync(key, Guid.NewGuid().ToString("n"));
            await db.StringGetAsync(key);
            Interlocked.Add(ref commands, 2);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // Expected: the soak severs connections on purpose. Counted, not fatal - what matters is that
            // traffic recovers, which the throughput check below covers.
            Interlocked.Increment(ref failures);
            await Task.Delay(10);
        }
    }
})).ToArray();

// ---- notification counting ---------------------------------------------------------------------------------
long events = 0;
muxer.ServerMaintenanceEvent += (_, e) =>
{
    if (e is PushMaintenanceEvent) Interlocked.Increment(ref events);
};

// ---- the loop ----------------------------------------------------------------------------------------------
var baseline = MemorySample.Take(0);
var samples = new List<MemorySample> { baseline };
var watch = Stopwatch.StartNew();
long expectedEvents = 0;
int seq = 0;

for (int cycle = 1; cycle <= cycles; cycle++)
{
    // Rotate through the shapes, so no single kind dominates and the pairs interleave.
    //
    // Note every Send returns how many opted-in clients it actually reached, and that is what counts as "sent"
    // - not the number of calls. A MOVING handoff deliberately replaces the connection, so a notification
    // issued during that instant has nobody to go to and returns zero. Counting calls instead reported a lost
    // event on a run that had lost nothing.
    switch (cycle % 5)
    {
        case 0:
            expectedEvents += host.Server.SendShardNotification(null, MaintenanceNotificationKind.Migrating, timeSeconds: 1, shardIds: "[\"1\"]", sequenceId: seq++);
            expectedEvents += host.Server.SendShardNotification(null, MaintenanceNotificationKind.Migrated, timeSeconds: null, shardIds: "[\"1\"]", sequenceId: seq++);
            break;
        case 1:
            expectedEvents += host.Server.SendSlotNotification(null, MaintenanceNotificationKind.SlotMigrating, "0-100", sequenceId: seq++);
            expectedEvents += host.Server.SendSlotMigrations(null, MaintenanceNotificationKind.SlotMigrated,
                [($"{host.EndPoint.Address}:{host.EndPoint.Port}", $"{host.EndPoint.Address}:{host.EndPoint.Port}", "0-100")], sequenceId: seq++);
            break;
        case 2:
            expectedEvents += host.Server.SendShardNotification(null, MaintenanceNotificationKind.FailingOver, timeSeconds: 1, shardIds: "[\"2\"]", sequenceId: seq++);
            expectedEvents += host.Server.SendShardNotification(null, MaintenanceNotificationKind.FailedOver, timeSeconds: null, shardIds: "[\"2\"]", sequenceId: seq++);
            break;
        case 3:
            // MOVING with no successor: the DNS/half-window path, and it replaces connections
            expectedEvents += host.Server.SendMoving(null, timeSeconds: 1, newEndpoint: null, sequenceId: seq++);
            break;
        default:
            // MOVING naming somewhere to go: the handoff-target path
            expectedEvents += host.Server.SendMoving(null, timeSeconds: 1, newEndpoint: host.EndPoint, sequenceId: seq++);
            break;
    }

    await Task.Delay(25);

    if (cycle % 50 == 0)
    {
        // Mid-storm the window *should* be open: notifications are arriving faster than it can expire, so an
        // endpoint that is not relaxed here means they are being received and ignored.
        invariants.Check(server.IsMaintenanceRelaxed, cycle, "timeouts were not relaxed during continuous notifications");

        // ...and then it must close once they stop. This is the check that needs the quiet: asking whether a
        // window has closed while still injecting only measures the injection rate, which is how the first
        // version of this reported a violation on every checkpoint of a perfectly healthy run.
        var settled = await SettlesAsync(() => !server.IsMaintenanceRelaxed, TimeSpan.FromSeconds(10));
        invariants.Check(settled, cycle, "the relaxed window never closed after notifications stopped (a stuck timeout)");

        // A handoff must never be left in flight either: the flag is what stops a second one starting, so a
        // leak means no future MOVING is ever acted on again - silently.
        invariants.Check(server.HandoffTarget is null, cycle, "a handoff target outlived its window (would pin this endpoint to one address)");

        // the dedup ring is fixed-size, so what is worth checking is that collapsing still *works* after
        // thousands of events rather than silently dropping everything
        var seen = Interlocked.Read(ref events);
        invariants.Check(seen > 0, cycle, "no notifications were raised at all");

        samples.Add(MemorySample.Take(cycle));
        var clients = host.Server.ClientCount;
        Console.WriteLine(
            $"  cycle {cycle,5}: events={seen,6} commands={Interlocked.Read(ref commands),8} " +
            $"failures={Interlocked.Read(ref failures),4} clients={clients,2} handoffs={server.HandoffRecycles,4} " +
            $"mem={samples[^1].Bytes / 1024,7}KB");

        // A recycle closes the old connection and opens one; the fake should not be accumulating them.
        invariants.Check(clients <= 8, cycle, $"the server is holding {clients} clients, which suggests connections are not being released");
    }
}

running.Cancel();
await Task.WhenAll(traffic);
watch.Stop();

// ---- report -------------------------------------------------------------------------------------------------
var first = samples.Count > 1 ? samples[1] : baseline; // after warmup
var last = samples[^1];
var growth = first.Bytes == 0 ? 0 : (last.Bytes - first.Bytes) * 100.0 / first.Bytes;

Console.WriteLine();
Console.WriteLine($"cycles:        {cycles} in {watch.Elapsed.TotalSeconds:0.0}s");
Console.WriteLine($"notifications: {Interlocked.Read(ref events)} raised, {expectedEvents} delivered");
Console.WriteLine($"commands:      {Interlocked.Read(ref commands)} ok, {Interlocked.Read(ref failures)} failed");
Console.WriteLine($"memory:        {first.Bytes / 1024}KB -> {last.Bytes / 1024}KB ({growth:+0.0;-0.0;0}%)");
Console.WriteLine($"clients:       {host.Server.ClientCount} now, {host.Server.TotalClientCount} over the run");

// Far fewer than the number of MOVINGs sent, and that is correct: a MOVING arriving while a handoff is already
// in flight is ignored, since starting a second poll against the same endpoint achieves nothing. Reported so
// the number is visible rather than assumed - a zero here would mean the handoff path never ran at all.
Console.WriteLine($"handoffs:      {server.HandoffRecycles} connection replacements");

// Growth is reported always and failed only when egregious: a soak that cries wolf on GC noise gets ignored,
// which is worse than not having one.
if (growth > 50 && last.Bytes - first.Bytes > 32 * 1024 * 1024)
{
    invariants.Record(cycles, $"managed memory grew {growth:0}% ({(last.Bytes - first.Bytes) / 1024 / 1024}MB) after warmup");
}

// Every notification the server delivered should be raised exactly once - with one bounded exception. A
// handoff replaces the connection, and a frame already queued for the socket it replaces is lost: the server
// counted a write, but nobody was left to read it. So loss is tolerated up to the number of connection
// replacements, and anything beyond that is a real leak in the receive path. Over-raising is never tolerated:
// it would mean the collapse has stopped collapsing.
var raised = Interlocked.Read(ref events);
var lost = expectedEvents - raised;
var handoffs = server.HandoffRecycles;
Console.WriteLine($"unraised:      {lost} (tolerance {handoffs}, one per connection replacement)");

if (raised > expectedEvents)
{
    invariants.Record(cycles, $"{raised} notifications raised for only {expectedEvents} delivered - the collapse is not collapsing");
}
else if (lost > handoffs)
{
    invariants.Record(cycles, $"{lost} delivered notifications were never raised, with only {handoffs} connection replacements to explain them");
}

if (server.HandoffRecycles == 0)
{
    invariants.Record(cycles, "no handoff ever ran, so this run says nothing about the handoff path");
}

if (invariants.Violations.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("PASS: no invariant violated");
    return 0;
}

Console.WriteLine();
Console.Error.WriteLine($"FAIL: {invariants.Violations.Count} invariant violation(s)");
foreach (var violation in invariants.Violations) Console.Error.WriteLine($"  {violation}");
return 1;

static async Task<bool> SettlesAsync(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (condition()) return true;
        await Task.Delay(100);
    }

    return condition();
}
