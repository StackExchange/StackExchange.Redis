using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// One run of a fault-injector scenario: setup, fire, tear down.
/// </summary>
/// <remarks>
/// The setup leg provisions its own database, which is the important part: every trigger publishes the
/// <c>dbconfig</c> it needs (<c>GET /topology-change-standalone?effect=...</c>), and all of them want
/// <c>proxy_policy: single</c> - a shape the environment templates do not create. So a scenario cannot be
/// pointed at an existing database, and there is no need to hand-roll <c>create_database</c> for one either.
/// <para>
/// Teardown is the whole reason this is a type rather than three calls. A scenario left set up holds cluster
/// state - the flags it enabled, the database it made, nodes it excluded - and poisons every run after it.
/// </para>
/// </remarks>
public sealed class ScenarioRun : IAsyncDisposable
{
    private readonly FaultInjectorClient _injector;
    private readonly string _scenario;
    private readonly Action<string> _log;

    private ScenarioRun(FaultInjectorClient injector, string scenario, string effect, string trigger, Action<string> log)
    {
        _injector = injector;
        _scenario = scenario;
        Effect = effect;
        Trigger = trigger;
        _log = log;
    }

    public string Effect { get; }

    public string Trigger { get; }

    /// <summary>The setup handle, passed back on the run and teardown legs.</summary>
    public string? SetupId { get; private set; }

    /// <summary>The database the setup leg created, when it says which.</summary>
    public int? BdbId { get; private set; }

    /// <summary>Whatever setup returned, for a test that wants to look at more than we model.</summary>
    public JsonElement SetupResult { get; private set; }

    /// <summary>The database setup created, ready to connect to.</summary>
    /// <remarks>
    /// Setup hands back everything needed - endpoint, password, TLS - so a scenario test never has to look in
    /// <c>endpoints.json</c> or ask the cluster REST API. Verified against a live run: the response carries
    /// <c>setup_id</c>, <c>bdb_id</c>, <c>db_name</c>, <c>endpoints</c>, <c>password</c>, <c>tls</c>,
    /// <c>mtls_files</c> and <c>config</c> (the proxy policy it chose to satisfy the trigger).
    /// </remarks>
    public ScenarioDatabase? Database { get; private set; }

    /// <summary>The database a scenario provisioned for itself.</summary>
    public sealed record ScenarioDatabase(string Name, int BdbId, string Host, int Port, bool Tls, string? Password, string? ProxyPolicy)
    {
        public override string ToString() => $"{Name} ({Host}:{Port}, bdb {BdbId}, policy {ProxyPolicy ?? "?"})";

        public ConfigurationOptions GetClientConfig(
            FaultInjectorEnvironment environment,
            MaintenanceNotificationMode mode = MaintenanceNotificationMode.Enabled)
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { { Host, Port } },
                Password = Password,
                Protocol = RedisProtocol.Resp3,
                MaintenanceNotifications = mode,
                AbortOnConnectFail = false,
                ConnectTimeout = 15_000,
                SyncTimeout = 15_000,
            };

            if (Tls)
            {
                options.Ssl = true;
                options.SslHost = Host;
                options.TrustIssuer(environment.CertificateAuthorityPath
                    ?? throw new InvalidOperationException($"{Name} uses TLS but no CA certificate was found in {environment.ConfigDirectory.FullName}"));
            }

            return options;
        }
    }

    public static async Task<ScenarioRun> SetupAsync(
        FaultInjectorClient injector,
        string scenario,
        string effect,
        string trigger,
        Action<string> log,
        IReadOnlyDictionary<string, string?>? extra = null,
        CancellationToken cancellationToken = default)
    {
        var run = new ScenarioRun(injector, scenario, effect, trigger, log);
        var query = new Dictionary<string, string?> { ["effect"] = effect, ["trigger"] = trigger };
        if (extra is not null)
        {
            foreach (var pair in extra) query[pair.Key] = pair.Value;
        }

        log($"setup {scenario}: effect={effect} trigger={trigger}");
        run.SetupResult = await injector.PostScenarioAsync(scenario, "setup", query, cancellationToken: cancellationToken);
        run.SetupId = FindString(run.SetupResult, "setup_id");
        run.BdbId = FindInt(run.SetupResult, "bdb_id");
        run.Database = ReadDatabase(run.SetupResult, run.BdbId);
        log($"setup complete: setup_id={run.SetupId ?? "(none)"} database={run.Database?.ToString() ?? "(none)"}");
        return run;
    }

    /// <summary>
    /// Fires the scenario and waits for the injector to finish its work.
    /// </summary>
    /// <remarks>
    /// Note "finished" here means the injector has done what it was asked, not that the deployment has settled:
    /// the notifications, the DNS change and the socket close all trail it by seconds. Callers should watch for
    /// what they expect rather than assuming completion means arrival.
    /// </remarks>
    public async Task<JsonElement> FireAsync(CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["effect"] = Effect,
            ["trigger"] = Trigger,
            ["setup_id"] = SetupId,
            ["bdb_id"] = BdbId?.ToString(),
        };

        _log($"firing {_scenario}");
        var result = await _injector.PostScenarioAsync(_scenario, leg: null, query, cancellationToken: cancellationToken);
        _log($"fired: {result}");
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        var query = new Dictionary<string, string?>
        {
            // both, because setup_id lives in the injector's memory and is lost if it restarts, at which point
            // bdb_id is the only handle left
            ["setup_id"] = SetupId,
            ["bdb_id"] = BdbId?.ToString(),
            ["restore_nodes"] = "true", // put back anything the scenario excluded, or the next run starts degraded
        };

        try
        {
            // Its own budget rather than the caller's token: teardown has to happen even when the test was
            // cancelled or timed out, which is exactly when the caller's token is already dead.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await _injector.PostScenarioAsync(_scenario, "teardown", query, cancellationToken: timeout.Token);
            _log("teardown complete");
        }
        catch (Exception ex)
        {
            // loud, but not an exception: a teardown failure must not replace the test's own verdict
            _log($"TEARDOWN FAILED ({ex.Message}) - the cluster may be left with a scenario set up, and "
                + $"setup_id={SetupId ?? "(none)"} bdb_id={BdbId?.ToString() ?? "(none)"} is what to clean up by hand");
        }
    }

    private static ScenarioDatabase? ReadDatabase(JsonElement setup, int? bdbId)
    {
        if (bdbId is not { } id) return null;

        var endpoint = FindString(setup, "endpoints");
        if (endpoint is null) return null;

        var text = endpoint;
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) text = text[(scheme + 3)..];
        var colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text[(colon + 1)..], out var port)) return null;

        return new ScenarioDatabase(
            FindString(setup, "db_name") ?? $"bdb-{id}",
            id,
            text[..colon],
            port,
            setup.TryGetProperty("tls", out var tls) && tls.ValueKind == JsonValueKind.True,
            FindString(setup, "password"),
            FindString(setup, "config"));
    }

    private static string? FindString(JsonElement element, string name)
    {
        foreach (var candidate in Walk(element, name))
        {
            if (candidate.ValueKind == JsonValueKind.String) return candidate.GetString();
            if (candidate.ValueKind == JsonValueKind.Number) return candidate.ToString();
            if (candidate.ValueKind == JsonValueKind.Array && candidate.GetArrayLength() > 0
                && candidate[0].ValueKind == JsonValueKind.String)
            {
                return candidate[0].GetString(); // "endpoints" is a list even when there is one
            }
        }

        return null;
    }

    private static int? FindInt(JsonElement element, string name)
    {
        foreach (var candidate in Walk(element, name))
        {
            if (candidate.ValueKind == JsonValueKind.Number && candidate.TryGetInt32(out var value)) return value;
            if (candidate.ValueKind == JsonValueKind.String && int.TryParse(candidate.GetString(), out var parsed)) return parsed;
        }

        return null;
    }

    /// <summary>
    /// Yields every value for a property name at any depth, shallowest first.
    /// </summary>
    /// <remarks>
    /// Tolerant on purpose: the scenario responses are documented as prose and nest differently between legs, so
    /// searching beats asserting a shape - and a test that cannot find <c>setup_id</c> still has a logged
    /// payload to work from rather than a deserialization error.
    /// </remarks>
    private static IEnumerable<JsonElement> Walk(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out var direct)) yield return direct;
            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in Walk(property.Value, name)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in Walk(item, name)) yield return nested;
            }
        }
    }
}
