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
    /// Built as data rather than as a typed record on purpose: these key names are the injector's wire schema,
    /// which is documented only as prose, so they belong in one visible place where they can be corrected
    /// against the real thing (go-redis's <c>DatabaseConfig</c> is the closest reference implementation).
    /// Treat every name here as unverified until a real run accepts it.
    /// </remarks>
    public Dictionary<string, object?> ToCreateParameters(string name, int port)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["port"] = port,
            ["memory_size"] = 1024 * 1024 * 100, // 100MB: enough for a scenario, small enough to place anywhere
            ["replication"] = Replication,
            ["sharding"] = ShardCount > 1,
            ["shards_count"] = ShardCount,
            ["tls_mode"] = Tls ? "enabled" : "disabled",
        };

        if (OssCluster)
        {
            parameters["oss_cluster"] = true;
            // what CLUSTER SLOTS advertises; decides whether a TLS client can validate the targets it is given
            if (EndpointType is not null) parameters["oss_cluster_api_preferred_ip_type"] = EndpointType;
        }

        if (ProxyPolicy is not null) parameters["proxy_policy"] = ProxyPolicy;

        return parameters;
    }
}
