using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Provisions one database shape for the tests that need it, and takes it away afterwards.
/// </summary>
/// <remarks>
/// Per *shape*, not per test: creating a database on a real cluster is slow, so several test classes share one.
/// <para>
/// The three-state behaviour matters more than the provisioning. No environment configured, or the
/// <c>E2E_SCENARIO_TESTS</c> gate not set, means every test skips - which is the ordinary case for anybody
/// running the full traversal, and these tests create and delete real databases. But an environment that *is*
/// configured and meant, and then does not work, must **fail**: a suite that skips on a broken environment
/// reports success for tests that never ran, and you will trust it exactly when you should not. So the absent
/// case skips from the test body, and the broken case throws from here.
/// </para>
/// </remarks>
public abstract class FaultInjectorFixture(DatabaseShape shape) : IAsyncLifetime
{
    /// <summary>
    /// Shared by every database this suite creates, in every run.
    /// </summary>
    /// <remarks>
    /// The sweep that cleans up leaks from earlier runs matches on this and nothing else, so it can never touch
    /// a database somebody created by hand. Worth being strict about: the alternative is a test suite that
    /// deletes production-shaped things on a shared cluster.
    /// </remarks>
    public const string NamePrefix = "sertest-";

    private static readonly string RunId = Guid.NewGuid().ToString("n")[..6];

    private FaultInjectorClient? _injector;
    private string? _skipReason;

    public DatabaseShape Shape { get; } = shape;

    /// <summary>The database created for this fixture, once <see cref="InitializeAsync"/> has run.</summary>
    public ProvisionedDatabase? Database { get; private set; }

    public FaultInjectorEnvironment Environment =>
        FaultInjectorEnvironment.Current ?? throw new InvalidOperationException("no environment; call RequireAvailable first");

    public FaultInjectorClient Injector =>
        _injector ?? throw new InvalidOperationException("no injector; call RequireAvailable first");

    /// <summary>
    /// Skips the calling test when there is no environment to talk to.
    /// </summary>
    public void RequireAvailable()
    {
        if (_skipReason is not null) Assert.Skip(_skipReason);
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
            _skipReason = "set E2E_SCENARIO_TESTS=true to run against a real deployment (these tests create and delete databases)";
            return;
        }

        // configured and meant: from here on, problems are failures
        _injector = new FaultInjectorClient(FaultInjectorEnvironment.Current.InjectorUrl);
        await SweepOrphansAsync();
        Database = await CreateDatabaseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // unconditionally, and before anything else can throw: a database left behind holds a port and a slice
        // of cluster memory, and the next run collides with it
        if (Database is { } database && _injector is { } injector)
        {
            try
            {
                await injector.RunActionAsync("delete_database", new Dictionary<string, object?> { ["bdb_id"] = database.BdbId });
            }
            catch (Exception ex)
            {
                // never mask a test failure with a cleanup failure; the sweep will get it next time
                Console.WriteLine($"failed to delete {database.Name} (bdb {database.BdbId}): {ex.Message}");
            }
        }

        _injector?.Dispose();
    }

    /// <summary>
    /// Creates the database for this shape, working around port collisions the way go-redis has to.
    /// </summary>
    /// <remarks>
    /// Port collisions are common enough on a shared cluster that go-redis carries a dedicated
    /// <c>CreateDatabaseWithPortRetry</c> helper; this is the same idea. The base port is deliberately high and
    /// the walk is upward, so a collision costs one attempt rather than a failed run.
    /// </remarks>
    private async Task<ProvisionedDatabase> CreateDatabaseAsync()
    {
        // Well clear of the 13xxx range the scenario setups pick from, so our own databases are not competing
        // with theirs for ports in the first place.
        const int BasePort = 14500, Attempts = 8;
        var name = $"{NamePrefix}{Shape.Label}-{RunId}";
        Exception? last = null;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var port = BasePort + (attempt * 3);
            try
            {
                // nested under database_config, not flattened: the injector answers a flat payload with
                // "Invalid parameter 'database_config': got None"
                var result = await Injector.RunActionAsync(
                    "create_database",
                    new Dictionary<string, object?> { ["database_config"] = Shape.ToCreateParameters(name, port) });
                return ProvisionedDatabase.FromCreateResult(name, port, Shape, result, Environment);
            }
            catch (Exception ex) when (IsPortCollision(ex))
            {
                last = ex;
                Console.WriteLine($"create_database on port {port} collided, retrying higher");
            }
        }

        throw new InvalidOperationException($"could not create '{name}' after {Attempts} attempts", last);
    }

    /// <summary>
    /// Whether a create failure is worth trying a different port for.
    /// </summary>
    /// <remarks>
    /// Only port collisions are: everything else - a malformed config, a cluster with no capacity - fails
    /// identically on every port, and retrying eight times turns one clear error message into eight and delays
    /// the report by half a minute. The first version of this retried everything, and buried
    /// "missing shard_key_regex" under eight identical tracebacks.
    /// </remarks>
    private static bool IsPortCollision(Exception ex) =>
        // "port_unavailable" is what Redis Enterprise actually answers, with the prose "Unavailable or invalid
        // port" - which matched none of the phrases the first version of this looked for, so a perfectly
        // retryable collision failed the whole fixture instead of moving up a port.
        ex.Message.Contains("port_unavailable", StringComparison.OrdinalIgnoreCase)
        || (ex.Message.Contains("port", StringComparison.OrdinalIgnoreCase)
            && (ex.Message.Contains("in use", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("taken", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Best-effort removal of databases left behind by earlier runs.
    /// </summary>
    /// <remarks>
    /// Best-effort on purpose: listing databases needs the cluster REST API, which needs credentials this
    /// directory may not carry, and being unable to tidy up is not a reason to fail a run that could otherwise
    /// proceed. The per-fixture delete in <see cref="DisposeAsync"/> is the reliable path; this only catches
    /// leaks from a run that was killed.
    /// </remarks>
    private async Task SweepOrphansAsync()
    {
        var cluster = Environment.Cluster;
        if (cluster is null)
        {
            Console.WriteLine("no cluster credentials in env_output.json; skipping orphan sweep");
            return;
        }

        try
        {
            using var rest = new ClusterRestClient(cluster, Environment.CertificateAuthorityPath);
            foreach (var (bdbId, name) in await rest.ListDatabasesAsync())
            {
                if (!name.StartsWith(NamePrefix, StringComparison.Ordinal)) continue; // never anything but ours

                Console.WriteLine($"sweeping orphaned test database {name} (bdb {bdbId})");
                await Injector.RunActionAsync("delete_database", new Dictionary<string, object?> { ["bdb_id"] = bdbId });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"orphan sweep skipped: {ex.Message}");
        }
    }
}
