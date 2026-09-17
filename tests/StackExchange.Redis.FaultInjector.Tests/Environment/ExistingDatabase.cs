using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// One reachable endpoint of an <c>endpoints.json</c> entry.
/// </summary>
public sealed record DatabaseMember(string Host, int Port)
{
    public override string ToString() => $"{Host}:{Port}";
}

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

    /// <summary>
    /// Every endpoint the entry lists, in file order; always at least one, and <c>Members[0]</c> is always
    /// <see cref="Host"/>/<see cref="Port"/>.
    /// </summary>
    /// <remarks>
    /// Normally one, and then this says nothing <see cref="Host"/> does not. It matters for Active-Active,
    /// where the entry's <c>endpoints</c> are one *independent member database per cluster* rather than
    /// several routes to one database - so a client has to connect to each separately rather than handing
    /// both to one multiplexer.
    /// <para>
    /// Note <c>raw_endpoints</c> describes only member 0, so <see cref="ProxyPolicy"/>,
    /// <see cref="EndpointType"/> and <see cref="Addresses"/> are member-0 facts and are deliberately not
    /// per-member here.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DatabaseMember> Members { get; init; } = [];

    /// <summary>
    /// How many independent member databases this entry describes; more than one only for Active-Active.
    /// </summary>
    public int MemberCount => Members.Count;

    public override string ToString() => MemberCount > 1
        ? $"{Key} ({Host}:{Port}, bdb {BdbId}, {ProxyPolicy ?? "?"}, {AdvertisedAddressCount} addr, {MemberCount} members)"
        : $"{Key} ({Host}:{Port}, bdb {BdbId}, {ProxyPolicy ?? "?"}, {AdvertisedAddressCount} addr)";

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
        if (!TryReadInt32(element, "bdb_id", out var id)) return false;

        // "endpoints" holds "host:port" or a redis:// URI depending on how the environment was templated, and
        // it is an *array*: normally of one, but an Active-Active entry lists one member database per cluster.
        string? host = null;
        int port = 0;
        var members = new List<DatabaseMember>();
        if (element.TryGetProperty("endpoints", out var endpoints) && endpoints.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in endpoints.EnumerateArray())
            {
                if (item.GetString() is { } text && TryParseHostPort(text, out var memberHost, out var memberPort))
                {
                    members.Add(new DatabaseMember(memberHost, memberPort));
                }
            }

            if (members.Count > 0)
            {
                host = members[0].Host;
                port = members[0].Port;
            }
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
            if (port == 0) TryReadInt32(head, "port", out port);

            if (head.TryGetProperty("addr", out var addr) && addr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in addr.EnumerateArray())
                {
                    if (item.GetString() is { } address) addresses.Add(address);
                }
            }
        }

        if (host is null || port == 0) return false;

        // the invariant Members[0] == (Host, Port) has to hold on the raw_endpoints path too
        if (members.Count == 0) members.Add(new DatabaseMember(host, port));

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
            addresses)
        {
            Members = members,
        };
        return true;
    }

    /// <summary>
    /// Splits an endpoint into host and port, tolerating an optional <c>scheme://</c> prefix.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Uri"/>: the environment templates write a bare <c>host:port</c>, and a
    /// dotted hostname is a *valid URI scheme*, so <c>new Uri("redis-11616.c1.example.com:11616")</c> parses
    /// the whole hostname as the scheme and yields an empty host with no port - silently, which is worse than
    /// throwing.
    /// </remarks>
    private static bool TryParseHostPort(string text, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) text = text[(scheme + 3)..];

        var colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) return false;

        host = text[..colon];
        return host.Length > 0;
    }

    /// <summary>
    /// Reads an integer that may arrive as a JSON number or as a JSON string.
    /// </summary>
    /// <remarks>
    /// Not defensiveness: the environment template writes <c>bdb_id</c> as a *string* for the Active-Active
    /// entries and as a *number* for every other entry in the same file. Gson coerces that silently, which is
    /// why the Java tests never had to notice; <see cref="JsonElement.TryGetInt32"/> does not, so a strict read
    /// drops exactly the Active-Active databases - and drops them from a dictionary whose absence looks like an
    /// environment template that simply does not have them.
    /// </remarks>
    private static bool TryReadInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var property)) return false;

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Connection options for this database, with maintenance notifications required.
    /// </summary>
    /// <param name="environment">The environment, for the CA certificate when this database uses TLS.</param>
    /// <param name="mode">
    /// The maintenance-notification opt-in; <see cref="MaintenanceNotificationMode.Enabled"/> *requires* them
    /// and rejects a connection that cannot deliver them.
    /// </param>
    /// <param name="memberIndex">
    /// Which of <see cref="Members"/> to connect to; only an Active-Active entry has more than one.
    /// </param>
    public ConfigurationOptions GetClientConfig(
        FaultInjectorEnvironment environment,
        MaintenanceNotificationMode mode = MaintenanceNotificationMode.Enabled,
        int memberIndex = 0)
    {
        if (memberIndex < 0 || memberIndex >= MemberCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(memberIndex), memberIndex, $"{Key} has {MemberCount} member(s)");
        }

        var member = Members[memberIndex];
        var options = new ConfigurationOptions
        {
            EndPoints = { { member.Host, member.Port } },
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
            options.SslHost = member.Host;
            var caPath = environment.CertificateAuthorityPath
                ?? throw new InvalidOperationException($"{Key} uses TLS but no CA certificate was found in {environment.ConfigDirectory.FullName}");
            options.TrustIssuer(caPath);
        }

        return options;
    }
}
