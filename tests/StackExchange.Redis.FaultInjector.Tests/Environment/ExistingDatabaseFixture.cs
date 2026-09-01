using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Runs against the databases the environment template already created, rather than provisioning new ones.
/// </summary>
/// <remarks>
/// The cheap tier, and the right one to start from: it needs none of the <c>create_database</c> schema, so it
/// answers "does the client work against a real deployment" before anything depends on parameter names that are
/// documented only as prose.
/// </remarks>
public class ExistingDatabaseFixture : IAsyncLifetime
{
    private FaultInjectorClient? _injector;
    private string? _skipReason;

    public IReadOnlyDictionary<string, ExistingDatabase> Databases { get; private set; }
        = new Dictionary<string, ExistingDatabase>();

    public FaultInjectorEnvironment Environment =>
        FaultInjectorEnvironment.Current ?? throw new InvalidOperationException("no environment; call RequireAvailable first");

    public FaultInjectorClient Injector =>
        _injector ?? throw new InvalidOperationException("no injector; call RequireAvailable first");

    /// <summary>Skips the calling test when there is nothing to talk to.</summary>
    public void RequireAvailable()
    {
        if (_skipReason is not null) Assert.Skip(_skipReason);
    }

    /// <summary>
    /// The named database, or a skip if this environment does not have one.
    /// </summary>
    /// <remarks>
    /// A skip rather than a failure, deliberately: which databases exist is a property of the environment
    /// template somebody chose, so a template without an <c>oss_cluster</c> database is a reason not to run the
    /// cluster tests, not evidence of a bug.
    /// </remarks>
    public ExistingDatabase Require(string key)
    {
        RequireAvailable();
        if (!Databases.TryGetValue(key, out var database))
        {
            Assert.Skip($"this environment has no '{key}' database (found: {string.Join(", ", Databases.Keys)})");
        }

        return database!;
    }

    public async ValueTask InitializeAsync()
    {
        if (FaultInjectorEnvironment.Current is null)
        {
            _skipReason = FaultInjectorEnvironment.UnavailableReason ?? "no fault-injector environment configured";
            return;
        }

        if (!FaultInjectorEnvironment.IsEnabled)
        {
            _skipReason = "set E2E_SCENARIO_TESTS=true to run against a real deployment";
            return;
        }

        _injector = new FaultInjectorClient(FaultInjectorEnvironment.Current.InjectorUrl);
        Databases = ExistingDatabase.ReadAll(FaultInjectorEnvironment.Current);
        await EnsureMaintenanceNotificationsEnabledAsync();
    }

    public ValueTask DisposeAsync()
    {
        _injector?.Dispose();
        return default;
    }

    /// <summary>
    /// Turns on the cluster-level maintenance-notification flags.
    /// </summary>
    /// <remarks>
    /// A different thing from the per-connection opt-in, and both are required: the cluster flag decides whether
    /// <c>CLIENT MAINT_NOTIFICATIONS</c> exists at all, and the command opts one connection in. Without this a
    /// run fails at connect - correctly, since the tests ask for
    /// <see cref="MaintenanceNotificationMode.Enabled"/> - but for a reason that has nothing to do with the
    /// client, so it is worth setting rather than diagnosing.
    /// <para>
    /// Through the injector rather than a direct REST <c>PUT</c>, so the change leaves an action id behind and
    /// shows up in the injector's history like everything else. Best-effort: if the action type is not
    /// available on this build, the connect failure that follows says so clearly enough.
    /// </para>
    /// </remarks>
    private async Task EnsureMaintenanceNotificationsEnabledAsync()
    {
        var flags = new Dictionary<string, object?>
        {
            ["client_maint_notifications"] = true,       // proxy-routed databases
            ["oss_cluster_client_maint_notifications"] = true, // oss_cluster databases
        };

        try
        {
            await Injector.RunActionAsync("update_cluster_config", flags, timeout: TimeSpan.FromMinutes(2));
            Console.WriteLine("cluster maintenance-notification flags enabled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not set cluster maintenance-notification flags: {ex.Message}");
        }
    }
}
