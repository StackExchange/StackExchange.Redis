using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Every group-surface call written in the documentation must exist.
/// </summary>
/// <remarks>
/// <para>
/// The docs were migrated from <c>db.StringGet(key)</c> to <c>db.Strings.GetAsync(key)</c> wholesale,
/// which is a lot of method names typed by hand into markdown, where nothing checks them. A renamed or
/// retired member would leave the documentation quietly telling people to write code that does not
/// compile - and the first person to find out would be a reader, not us.
/// </para>
/// <para>
/// So: pull every <c>db.Group.MemberAsync(</c> out of the markdown and demand it resolves to a real
/// public member of that group. Cheap, and it turns "the docs are right" from a claim into a test.
/// </para>
/// </remarks>
public class DocsSurfaceTests(ITestOutputHelper log)
{
    /// <summary>Walk up from the test binaries until the repo marker turns up.</summary>
    /// <remarks>
    /// <b>Not <see cref="CallerFilePathAttribute"/></b>: this repo builds deterministically, so the
    /// compiler rewrites source paths to <c>/_/...</c> and the caller's path does not exist on disk. The
    /// binaries do, and they sit under the repo.
    /// </remarks>
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StackExchange.Redis.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>The group accessors the surface offers, and the static class that holds their commands.</summary>
    private static Dictionary<string, Type> Groups { get; } = typeof(Strings).Assembly
        .GetTypes()
        .Where(t => t.IsPublic && t.IsAbstract && t.IsSealed && t.Namespace == "StackExchange.Redis")
        .Where(t => typeof(Strings).Assembly.GetType("StackExchange.Redis.Resp" + t.Name) is not null)
        .ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);

    public static IEnumerable<object[]> MarkdownFiles()
    {
        var root = RepoRoot();
        if (root is null) yield break; // not a source checkout; the theory simply has no cases
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
        {
            // not Path.GetRelativePath: it does not exist on net481, which this project also targets
            yield return [file.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/')];
        }

        yield return ["README.md"];
    }

    [Theory, MemberData(nameof(MarkdownFiles))]
    public void EveryGroupCallInTheDocsResolves(string relativePath)
    {
        var root = RepoRoot();
        Assert.SkipWhen(root is null, "not running from a source checkout");
        var text = File.ReadAllText(Path.Combine(root!, relativePath));

        // db.Strings.GetAsync( / batch.Hashes.SetAsync( / server.Keyspace.CountAsync(
        //
        // The receiver is restricted to the handful of names the documentation actually uses, because a
        // group name can be an ordinary word: docs/Failover.md has `context.Server.InventKey(...)`, which
        // is a test helper and nothing to do with the server command group.
        var matches = Regex.Matches(text, @"\b(?:db|database|batch|tran|transaction|prefixed|server)\.(?<group>[A-Z]\w+)\.(?<member>[A-Z]\w+)\(");
        int checkedCount = 0;
        var missing = new List<string>();

        foreach (Match match in matches)
        {
            var group = match.Groups["group"].Value;
            if (!Groups.TryGetValue(group, out var type)) continue; // not a group accessor; not ours to check

            checkedCount++;
            var member = match.Groups["member"].Value;
            if (type.GetMember(member, BindingFlags.Public | BindingFlags.Static).Length == 0)
            {
                missing.Add($"{group}.{member}");
            }
        }

        log.WriteLine($"{relativePath}: {checkedCount} group call(s) checked");
        Assert.Empty(missing.Distinct().OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void TheGroupListItselfIsNotEmpty()
    {
        // the test above passes vacuously if the reflection above finds nothing, which is exactly the
        // failure a rename would cause - so pin the discovery, not just the lookups
        Assert.Contains("Strings", Groups.Keys);
        Assert.Contains("Hashes", Groups.Keys);
        Assert.True(Groups.Count >= 13, $"only found {Groups.Count} groups");
    }
}
