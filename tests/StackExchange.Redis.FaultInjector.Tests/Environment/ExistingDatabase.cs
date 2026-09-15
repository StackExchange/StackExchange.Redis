using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// A database the environment already provisioned, read from <c>endpoints.json</c>.
/// </summary>
/// <remarks>
/// The counterpart to creating our own. Provisioning gives control over the shape, which is what the matrix
/// needs; this gives a run against whatever the environment template made, which is what you want when the
/// question is "does any of this work at all" rather than "does it work for shape X".
/// <para>
/// Note <c>endpoints.json</c> carries more than expected: each entry's <c>raw_endpoints</c> includes
/// <c>proxy_policy</c>, <c>oss_cluster_api_preferred_endpoint_type</c> and the address list behind the DNS
/// name. So the facts that decide client behaviour are mostly here, and the cluster REST API is only needed for
/// the explicit <c>oss_cluster</c> flag and the client-certificate settings.
/// </para>
/// </remarks>
public sealed record ExistingDatabase(
    string Key,
    int BdbId,
    string Host,
    int Port,
    bool Tls,
    string? Username,
    string? Password,
    string? ProxyPolicy,
    string? EndpointType,
    IReadOnlyList<string> Addresses)
{
    /// <summary>
    /// How many addresses the hostname is expected to resolve to.
    /// </summary>
    /// <remarks>
    /// The measured driver of handoff behaviour: with more than one address a live sibling always exists, so a
    /// handoff steps sideways immediately; with one, it has to wait for the record to move. Tests that care
    /// should assert on this rather than on the policy name, because the count follows actual proxy
    /// *placement* - an <c>all-master-shards</c> database whose shards share a node advertises one address.
    /// </remarks>
    public int AdvertisedAddressCount => Addresses.Count;

    public override string ToString() => $"{Key} ({Host}:{Port}, bdb {BdbId}, {ProxyPolicy ?? "?"}, {AdvertisedAddressCount} addr)";

    /// <summary>
    /// Reads every database in the environment's <c>endpoints.json</c>, keyed as that file keys them.
    /// </summary>
    public static Dictionary<string, ExistingDatabase> ReadAll(FaultInjectorEnvironment environment)
    {
        var results = new Dictionary<string, ExistingDatabase>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(environment.ConfigDirectory.FullName, "endpoints.json");
        if (!File.Exists(path)) return results;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) continue;
            if (TryRead(entry.Name, entry.Value, out var database)) results[entry.Name] = database;
        }

        return results;
    }

    private static bool TryRead(string key, JsonElement element, out ExistingDatabase database)
    {
        database = null!;
        if (!element.TryGetProperty("bdb_id", out var bdbId) || !bdbId.TryGetInt32(out var id)) return false;

        // "endpoints" holds "host:port" or a redis:// URI depending on how the environment was templated
        string? host = null;
        int port = 0;
        if (element.TryGetProperty("endpoints", out var endpoints) && endpoints.ValueKind == JsonValueKind.Array
            && endpoints.GetArrayLength() > 0 && endpoints[0].GetString() is { } first)
        {
            var text = first;
            var scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) text = text[(scheme + 3)..];
            var colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text[(colon + 1)..], out port)) host = text[..colon];
        }

        string? proxyPolicy = null, endpointType = null;
        var addresses = new List<string>();
        if (element.TryGetProperty("raw_endpoints", out var raw) && raw.ValueKind == JsonValueKind.Array
            && raw.GetArrayLength() > 0)
        {
            var head = raw[0];
            proxyPolicy = ReadString(head, "proxy_policy");
            endpointType = ReadString(head, "oss_cluster_api_preferred_endpoint_type");
            host ??= ReadString(head, "dns_name");
            if (port == 0 && head.TryGetProperty("port", out var rawPort)) rawPort.TryGetInt32(out port);

            if (head.TryGetProperty("addr", out var addr) && addr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in addr.EnumerateArray())
                {
                    if (item.GetString() is { } address) addresses.Add(address);
                }
            }
        }

        if (host is null || port == 0) return false;

        database = new ExistingDatabase(
            key,
            id,
            host,
            port,
            element.TryGetProperty("tls", out var tls) && tls.ValueKind == JsonValueKind.True,
            ReadString(element, "username"),
            ReadString(element, "password"),
            proxyPolicy,
            endpointType,
            addresses);
        return true;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Connection options for this database, with maintenance notifications required.
    /// </summary>
    public ConfigurationOptions GetClientConfig(FaultInjectorEnvironment environment, MaintenanceNotificationMode mode = MaintenanceNotificationMode.Enabled)
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { { Host, Port } },
            User = string.Equals(Username, "default", StringComparison.OrdinalIgnoreCase) ? null : Username,
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
            var caPath = environment.CertificateAuthorityPath
                ?? throw new InvalidOperationException($"{Key} uses TLS but no CA certificate was found in {environment.ConfigDirectory.FullName}");
            options.TrustIssuer(caPath);
        }

        return options;
    }
}
