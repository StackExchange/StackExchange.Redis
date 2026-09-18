using System;
using System.Collections.Generic;
using System.Text.Json;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// A database this suite created, and how to connect to it.
/// </summary>
/// <remarks>
/// Note what this type does *not* need to do: read <c>endpoints.json</c> to discover whether the database is
/// <c>oss_cluster</c>, or what endpoint type it advertises. A test that provisioned the database knows what it
/// asked for, which is the real argument for provisioning from inside the suite - the alternative is carrying
/// those facts from whoever set the environment up into the run, by hand.
/// </remarks>
public sealed class ProvisionedDatabase
{
    private ProvisionedDatabase(string name, int bdbId, string host, int port, DatabaseShape shape, FaultInjectorEnvironment environment)
    {
        Name = name;
        BdbId = bdbId;
        Host = host;
        Port = port;
        Shape = shape;
        Environment = environment;
    }

    public string Name { get; }

    public int BdbId { get; }

    public string Host { get; }

    public int Port { get; }

    public DatabaseShape Shape { get; }

    public FaultInjectorEnvironment Environment { get; }

    public string? Password { get; private init; }

    /// <summary>
    /// Connection options for this database, with maintenance notifications requested.
    /// </summary>
    /// <remarks>
    /// <see cref="MaintenanceNotificationMode.Enabled"/> rather than <c>Auto</c>: in a test, a server that
    /// silently declines the opt-in should fail the connection loudly rather than produce a run that passes
    /// while observing nothing. That is the opposite of the right default for production, and exactly right
    /// here.
    /// </remarks>
    public ConfigurationOptions GetClientConfig()
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { { Host, Port } },
            Password = Password,
            Protocol = RedisProtocol.Resp3,
            MaintenanceNotifications = MaintenanceNotificationMode.Enabled,
            AbortOnConnectFail = false,
            // real network, real cluster: connect and command budgets have to tolerate a WAN round trip, and a
            // cluster that is mid-scenario is slower still
            ConnectTimeout = 15_000,
            SyncTimeout = 15_000,
        };

        if (Shape.Tls)
        {
            options.Ssl = true;
            options.SslHost = Host;

            // The certificates are self-signed per environment, so trusting the issuer is what makes a TLS test
            // mean anything. If the CA is missing we fail here rather than disabling validation: a TLS test that
            // quietly stops checking identity reports success for the one thing it exists to catch.
            var caPath = Environment.CertificateAuthorityPath
                ?? throw new InvalidOperationException(
                    $"{Shape.Label} needs TLS, but no CA certificate was found in {Environment.ConfigDirectory.FullName}; "
                    + "validation is not disabled for tests");
            options.TrustIssuer(caPath);
        }

        return options;
    }

    /// <summary>Masked, because these endpoints are real and reachable from the internet.</summary>
    public override string ToString() => $"{Name} ({Host}:{Port}, bdb {BdbId}, {Shape.Label})";

    /// <summary>
    /// Reads what the injector reported back after <c>create_database</c>.
    /// </summary>
    /// <remarks>
    /// Tolerant by design: the response shape is documented as prose, and the fields worth having may arrive at
    /// the top level or nested under an output/result object. What cannot be guessed is the <c>bdb_id</c> -
    /// without it there is nothing to delete afterwards - so that one is required and its absence is loud.
    /// </remarks>
    public static ProvisionedDatabase FromCreateResult(
        string name,
        int requestedPort,
        DatabaseShape shape,
        JsonElement result,
        FaultInjectorEnvironment environment)
    {
        var bdbId = FindInt(result, "bdb_id")
            ?? throw new InvalidOperationException($"create_database for '{name}' returned no bdb_id: {result}");

        var host = FindString(result, "endpoint") ?? FindString(result, "dns_name") ?? environment.Cluster?.ClusterName
            ?? throw new InvalidOperationException($"create_database for '{name}' returned no endpoint, and env_output.json names no cluster: {result}");

        // an endpoint may arrive as "host:port" or as a bare host
        var port = requestedPort;
        var colon = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host[(colon + 1)..], out var parsedPort))
        {
            port = parsedPort;
            host = host[..colon];
        }

        return new ProvisionedDatabase(name, bdbId, host, port, shape, environment)
        {
            Password = FindString(result, "password"),
        };
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

    private static string? FindString(JsonElement element, string name)
    {
        foreach (var candidate in Walk(element, name))
        {
            if (candidate.ValueKind == JsonValueKind.String) return candidate.GetString();
            if (candidate.ValueKind == JsonValueKind.Array && candidate.GetArrayLength() > 0 && candidate[0].ValueKind == JsonValueKind.String)
            {
                return candidate[0].GetString(); // endpoints arrive as a list often enough
            }
        }

        return null;
    }

    /// <summary>
    /// Yields every value for a property name, at any depth - shallowest first.
    /// </summary>
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
