using System;
using System.Collections.Generic;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The database shapes this suite provisions, described declaratively.
/// </summary>
/// <remarks>
/// Shape is the axis that matters, and it is not cosmetic: measurements on 2026-08-28 showed the number of A
/// records a hostname carries follows actual proxy *placement*, giving 2 for <c>all-nodes</c>, 3 for
/// <c>all-master-shards</c> and 1 for <c>single</c> - and the handoff takes a different branch depending on
/// whether a live sibling address exists. So the shapes below are the reason to provision databases from inside
/// the tests at all: they turn that into a matrix axis instead of a manual sweep.
/// </remarks>
public sealed record DatabaseShape(
    string Label,
    bool OssCluster = false,
    string? ProxyPolicy = null,
    bool Tls = false,
    bool Replication = false,
    int ShardCount = 1,
    string? EndpointType = null)
{
    /// <summary>A proxied standalone database: the shape <c>MOVING</c> actually fires on.</summary>
    public static readonly DatabaseShape ProxiedStandalone = new("proxied-standalone", ProxyPolicy: "single");

    /// <summary>Multiple proxies, so the hostname carries several A records and a sibling always exists.</summary>
    public static readonly DatabaseShape MultiProxy = new("multi-proxy", ProxyPolicy: "all-master-shards", ShardCount: 2);

    /// <summary>OSS cluster API: the family that emits <c>SMIGRATING</c>/<c>SMIGRATED</c> instead.</summary>
    public static readonly DatabaseShape OssClusterApi = new("oss-cluster", OssCluster: true, ProxyPolicy: "all-master-shards", ShardCount: 2);

    /// <summary>
    /// TLS with hostname-advertised endpoints - the documented coverage gap.
    /// </summary>
    /// <remarks>
    /// <c>endpoint_type: ip</c> is the interesting counterpart: with addresses advertised, a verifying client
    /// cannot check identity on the targets it is told to use, because the proxy certificate carries DNS names
    /// and no IP SAN. That pairing is the thing no in-process harness can honestly reproduce.
    /// </remarks>
    public static readonly DatabaseShape TlsWithHostnames = new("tls-hostnames", OssCluster: true, Tls: true, ShardCount: 2, EndpointType: "hostname");

    /// <summary>
    /// The <c>create_database</c> parameters for this shape.
    /// </summary>
    /// <remarks>
    /// Goes inside a <c>database_config</c> wrapper when it reaches the injector - a flat payload is rejected
    /// with "Invalid parameter 'database_config': got None".
    /// <para>
    /// Built as data rather than as a typed record, because these are the injector's wire names and its schema
    /// declares <c>parameters</c> as untyped. The names here are no longer guesses: they match the environment's
    /// own <c>bdb_config.json</c> and the <c>dbconfig</c> the injector itself publishes as a trigger requirement
    /// (<c>GET /topology-change-standalone?effect=...</c>), which is the authoritative list.
    /// </para>
    /// </remarks>
    public Dictionary<string, object?> ToCreateParameters(string name, int port)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["port"] = port,
            ["memory_size"] = 134_217_728, // 128MB, matching what the injector asks for in its own requirements
            ["eviction_policy"] = "volatile-lru",
            ["replication"] = Replication,
            ["sharding"] = ShardCount > 1,
            ["shards_count"] = ShardCount,
            ["shards_placement"] = "sparse", // spreads shards across nodes, which is what makes proxy policy visible
            ["oss_cluster"] = OssCluster,
        };

        if (ShardCount > 1)
        {
            // Required whenever sharding is on: Redis Enterprise rejects the database outright with
            // "Invalid sharding configuration: missing shard_key_regex". These two patterns are the standard
            // pair - an explicit hash tag if present, otherwise the whole key - and are what the environment's
            // own bdb_config.json uses.
            parameters["shard_key_regex"] = new[]
            {
                new Dictionary<string, object?> { ["regex"] = ".*\\{(?<tag>.*)\\}.*" },
                new Dictionary<string, object?> { ["regex"] = "(?<tag>.*)" },
            };
        }

        // only when asked for: a database with no tls_mode serves plaintext, and setting it here without
        // certificates in place produces a database nothing can connect to
        if (Tls) parameters["tls_mode"] = "enabled";

        if (OssCluster && EndpointType is not null)
        {
            // Two distinct axes, easily conflated. "endpoint_type" is ip-versus-hostname - what CLUSTER SLOTS
            // advertises, and therefore whether a verifying TLS client can check identity on the targets it is
            // given. "ip_type" is internal-versus-external, which is about routing rather than identity.
            parameters["oss_cluster_api_preferred_endpoint_type"] = EndpointType;
        }

        if (ProxyPolicy is not null) parameters["proxy_policy"] = ProxyPolicy;

        return parameters;
    }
}
