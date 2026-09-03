using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The cluster's node list, read through the fault injector rather than the cluster's own API.
/// </summary>
/// <remarks>
/// The management API on port 9443 is not reachable from outside the deployment's network - measured: a socket
/// error, not an authentication one - so anything a test needs to know about nodes has to come back the same
/// way it gives instructions. <c>execute_rladmin_command</c> runs cluster-side and returns stdout, which makes
/// <c>status nodes</c> the portable source of truth.
/// <para>
/// Text parsing is not lovely, but it is honest about where the information comes from, and the alternative -
/// hardcoding node ids - is what made the first destructive run prove nothing.
/// </para>
/// </remarks>
internal static class ClusterNodes
{
    internal sealed record Node(int Id, string Role, string Address, string ExternalAddress);

    /// <summary>
    /// Runs <c>rladmin status nodes</c> and returns what it said.
    /// </summary>
    /// <remarks>
    /// <paramref name="bdbId"/> is required by the action even though the command is cluster-wide: the
    /// injector resolves a database to decide where to run.
    /// </remarks>
    public static async Task<List<Node>> ListAsync(FaultInjectorClient injector, int bdbId, CancellationToken cancellationToken)
    {
        var result = await injector.RunActionAsync(
            "execute_rladmin_command",
            new Dictionary<string, object?>
            {
                ["bdb_id"] = bdbId.ToString(),
                ["rladmin_command"] = "status nodes",
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // the action's payload nests the command's stdout under output.output
        var text = result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("output", out var inner)
            && inner.ValueKind == JsonValueKind.Object
            && inner.TryGetProperty("output", out var stdout)
                ? stdout.GetString()
                : null;

        return text is null ? [] : Parse(text);
    }

    /// <summary>
    /// Which node currently answers for this hostname, or <c>null</c> if the addresses do not match any node.
    /// </summary>
    /// <remarks>
    /// The step that makes a node-scoped fault mean anything: killing an arbitrary node usually proves
    /// nothing, because the deployment absorbs it and the client never notices.
    /// </remarks>
    public static async Task<Node?> FindServingAsync(
        FaultInjectorClient injector,
        int bdbId,
        string host,
        CancellationToken cancellationToken)
    {
        var nodes = await ListAsync(injector, bdbId, cancellationToken).ConfigureAwait(false);
        var addresses = (await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false))
            .Select(a => a.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return nodes.FirstOrDefault(n =>
            addresses.Contains(n.ExternalAddress) || addresses.Contains(n.Address));
    }

    /// <summary>
    /// Reads the fixed-column output of <c>status nodes</c>.
    /// </summary>
    /// <remarks>
    /// The leading <c>*</c> marks the node the command ran against, so it is stripped rather than parsed.
    /// </remarks>
    internal static List<Node> Parse(string output)
    {
        var nodes = new List<Node>();
        foreach (var line in output.Split('\n'))
            {
            var match = Regex.Match(
                line.Trim(),
                @"^\*?node:(?<id>\d+)\s+(?<role>\S+)\s+(?<addr>\S+)\s+(?<ext>\S+)");
            if (match.Success && int.TryParse(match.Groups["id"].Value, out var id))
            {
                nodes.Add(new Node(id, match.Groups["role"].Value, match.Groups["addr"].Value, match.Groups["ext"].Value));
            }
        }

        return nodes;
    }
}
