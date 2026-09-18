using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Telling "this deployment cannot do that" apart from "the client got it wrong".
/// </summary>
/// <remarks>
/// The effect/trigger matrix is sparse in ways that depend on the *cluster*, not just the database: an
/// <c>add</c> migration needs a node holding more than one shard, and a three-node cluster with sparse shard
/// placement gives every node exactly one. The injector reports that as a failed action with a Python
/// traceback, which is indistinguishable from a real fault unless you read the message.
/// <para>
/// So these skip, narrowly and by message. A broad catch here would hide genuine injector failures, which is
/// the thing this tier exists to surface - hence matching on the specific condition rather than on the
/// exception type.
/// </para>
/// </remarks>
internal static class ScenarioSupport
{
    private static readonly string[] PlacementLimitations =
    [
        "No node with multiple shards found",
        "not enough nodes",
        "no empty node",
    ];

    /// <summary>
    /// Skips when the setup could not produce a database the effect can act on.
    /// </summary>
    public static void RequireEffectIsAchievable(ScenarioRun scenario, string effect)
    {
        if (scenario.Database is null)
        {
            Assert.Skip($"the injector did not provision a database for '{effect}'");
        }
    }

    /// <summary>
    /// Fires a scenario, skipping rather than failing when the cluster's shape cannot produce the effect.
    /// </summary>
    public static async Task FireOrSkipAsync(ScenarioRun scenario, string effect, CancellationToken cancellationToken)
    {
        try
        {
            await scenario.FireAsync(cancellationToken);
        }
        catch (Exception ex) when (IsPlacementLimitation(ex))
        {
            Assert.Skip($"this cluster cannot produce '{effect}': {Summarize(ex.Message)}");
        }
    }

    private static bool IsPlacementLimitation(Exception ex)
    {
        foreach (var limitation in PlacementLimitations)
        {
            if (ex.Message.Contains(limitation, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// The part of a Python traceback that says what actually went wrong.
    /// </summary>
    /// <remarks>
    /// Note the traceback arrives inside a JSON string, so its line breaks are the two characters
    /// <c>\</c><c>n</c> rather than real newlines - splitting on <c>'\n'</c> alone matches nothing and returns
    /// the whole wall of text, which is how the first version of this behaved. The injector helpfully ends with
    /// "Caused by: ...", which is the only part worth putting in a skip message.
    /// </remarks>
    internal static string Summarize(string message)
    {
        var caused = message.LastIndexOf("Caused by:", StringComparison.Ordinal);
        if (caused >= 0)
        {
            var tail = message[caused..];
            var end = tail.IndexOf("\\n", StringComparison.Ordinal);
            return end > 0 ? tail[..end] : tail;
        }

        var lines = message.Replace("\\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Contains("Exception:", StringComparison.Ordinal)) return lines[i];
        }

        return lines.Length > 0 ? lines[^1] : message;
    }
}
