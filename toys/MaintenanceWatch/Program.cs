using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using StackExchange.Redis.Maintenance;

// Watches a real deployment for server-native maintenance notifications and prints what we made of them.
//
// The point of this tool is *evidence*: that we asked for notifications, that they arrive, and that each one
// was understood - so every event prints both our parsed view and the raw payload it came from, and any
// disagreement between the two is visible rather than inferred.
//
//   dotnet run --project toys/MaintenanceWatch -- "my-endpoint:6379,user=...,password=..."
//
// Notes:
// - RESP3 is required; this forces it rather than relying on the endpoint's provider defaults.
// - The opt-in mode is forced to Auto, so an endpoint that does not support notifications still connects.
// - Nothing here reacts to a notification; observing is the whole job.
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: MaintenanceWatch <connection-string> [--enabled]");
    return 1;
}

var config = ConfigurationOptions.Parse(args[0]);
config.Protocol = RedisProtocol.Resp3;
config.MaintenanceNotifications = args.Contains("--enabled")
    ? MaintenanceNotificationMode.Enabled // refuse to run if the server will not deliver
    : MaintenanceNotificationMode.Auto;
config.AbortOnConnectFail = false;

// note ToString() includes the password by default; the library's own logging masks it (see
// LoggerExtensions), but this line is ours, so it has to ask
Console.WriteLine($"connecting: {config.ToString(includePassword: false)}");
Console.WriteLine($"defaults provider: {config.Defaults}");
Console.WriteLine($"maintNotifications={config.MaintenanceNotifications}, relaxed={config.MaintenanceRelaxedTimeout.TotalSeconds}s," +
    $" windowMax={config.MaintenanceRelaxedWindowMax.TotalSeconds}s, postEvent={config.MaintenancePostEventRelaxedDuration.TotalSeconds}s");

// the connect log carries the negotiation half: whether the opt-in was sent, and how the server answered
await using var conn = await ConnectionMultiplexer.ConnectAsync(config, Console.Out);

var count = 0;
conn.ServerMaintenanceEvent += (_, e) =>
{
    var n = Interlocked.Increment(ref count);
    Console.WriteLine();
    Console.WriteLine($"=== maintenance event #{n} at {DateTime.UtcNow:HH:mm:ss.fff}Z ===");
    Console.WriteLine($"  raw:  {e.RawMessage}");

    if (e is not PushMaintenanceEvent push)
    {
        // e.g. the Azure pub/sub channel; not what this tool is for, but worth seeing rather than hiding
        Console.WriteLine($"  (not a push notification: {e.GetType().Name})");
        return;
    }

    Console.WriteLine($"  type: {push.NotificationType}   seq: {push.SequenceId}   from: {push.EndPoint}");
    Console.WriteLine($"  time: {(push.Time is { } t ? $"{t.TotalSeconds}s" : "(none)")}" +
        $"   startsAt: {(push.StartTimeUtc is { } at ? $"{at:HH:mm:ss}Z" : "(n/a)")}");

    if (push.NotificationType == MaintenanceNotificationType.Moving)
    {
        // null is a documented outcome, not a parse failure: reconnect to what you already have
        Console.WriteLine($"  moving to: {push.NewEndPoint?.ToString() ?? "(no address given)"}");
    }

    if (push.Payload is { } payload)
    {
        Console.WriteLine($"  payload: {payload}");
    }

    foreach (var migration in push.SlotMigrations)
    {
        var slots = migration.Slots.Count == 0
            ? $"(unparsed: '{migration.RawSlots}')"
            : string.Join(",", migration.Slots.Select(x => x.From == x.To ? $"{x.From}" : $"{x.From}-{x.To}"));
        Console.WriteLine($"  slots {slots}: {migration.Source?.ToString() ?? "?"} -> {migration.Target?.ToString() ?? "?"}");
    }
};

// a fault during an announced disruption says so, which is the other half of "we understood it"
conn.ErrorMessage += (_, e) => Console.WriteLine($"[error] {e.EndPoint}: {e.Message}");
conn.ConnectionFailed += (_, e) => Console.WriteLine($"[failed] {e.EndPoint}: {e.FailureType} {e.Exception?.Message}");
conn.ConnectionRestored += (_, e) => Console.WriteLine($"[restored] {e.EndPoint}");

Console.WriteLine();
Console.WriteLine("watching; a little traffic keeps the connection interesting. Ctrl+C to stop.");

var db = conn.GetDatabase();
var key = $"maintenance-watch:{Guid.NewGuid():N}";
while (true)
{
    try
    {
        await db.StringSetAsync(key, (RedisValue)DateTime.UtcNow.Ticks);
        _ = await db.StringGetAsync(key);
    }
    catch (Exception ex) when (ex is RedisException or TimeoutException)
    {
        // note RedisTimeoutException derives from TimeoutException, *not* RedisException - so a
        // `catch (RedisException)` would silently miss exactly the case this tool exists to show
        // MaintenanceType is the payoff here: "timeout" versus "timeout during an announced failover"
        var maintenance = ex switch
        {
            RedisTimeoutException timeout => timeout.MaintenanceType,
            RedisConnectionException connection => connection.MaintenanceType,
            _ => MaintenanceNotificationType.None,
        };
        Console.WriteLine($"[command] {ex.GetType().Name}: {ex.Message}" +
            (maintenance == MaintenanceNotificationType.None ? "" : $"  <- during {maintenance}"));
    }

    await Task.Delay(1000);
}
