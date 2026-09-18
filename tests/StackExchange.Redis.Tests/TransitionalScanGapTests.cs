using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The cursor scans on <see cref="IDatabase"/>, which SER352 cannot see.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AutoDatabase]</c> skips every <c>IEnumerable&lt;T&gt;</c>/<c>IAsyncEnumerable&lt;T&gt;</c> member -
/// deferred execution does not fit capture-and-replay - so these are hand-written, and the generator has
/// no way to tell a real implementation from a forwarding throw. To it they are simply "declared", so the
/// SER352 count omits them entirely. This file is the tripwire in the one place that can see them.
/// </para>
/// <para>
/// It earned that description immediately: wiring the scans to the context surface made the previous
/// version of this test fail, which is exactly what a tripwire on an uncounted gap is for.
/// </para>
/// </remarks>
public class TransitionalScanGapTests
{
    private sealed class PageExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
            => RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static IDatabase Target(params string[] replies)
        => new TransitionalDatabase(
            new RespDatabaseContext(new RespContext().WithExecutor(new PageExecutor(replies))),
            null!,
            null);

    private static string Page(long cursor, params string[] items)
    {
        var sb = new StringBuilder($"*2\r\n:{cursor}\r\n*{items.Length}\r\n");
        foreach (var item in items) sb.Append($"${item.Length}\r\n{item}\r\n");
        return sb.ToString();
    }

    private static IEnumerable<MethodInfo> ScanMembers() =>
        typeof(TransitionalDatabase)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsGenericType)
            .Where(m => m.ReturnType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                     || m.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));

    /// <summary>The size of the uncounted family, so implementing one forces this number to move.</summary>
    [Fact]
    public void TheScanFamilyIsThisBig() => Assert.Equal(13, ScanMembers().Count());

    /// <summary>
    /// What is still deferred: the vector-set enumerates, which are not cursor scans at all.
    /// </summary>
    /// <remarks>
    /// They are keyset pagination over <c>VRANGE</c> - the shipped code says it avoids "scan" naming "in
    /// case a VSCAN command is added later" - so they need none of the cursor machinery and were not moved
    /// with it. They are in this list only because they happen to return <see cref="IEnumerable{T}"/> and
    /// so fall in the same skipped bucket.
    /// </remarks>
    [Theory]
    [InlineData(nameof(IDatabase.VectorSetRangeEnumerate))]
    [InlineData(nameof(IDatabaseAsync.VectorSetRangeEnumerateAsync))]
    public void TheVectorSetEnumeratesStillThrow(string name)
    {
        var method = ScanMembers().Single(m => m.Name == name);
        var args = method.GetParameters()
            .Select(p => p.HasDefaultValue ? p.DefaultValue : Activator.CreateInstance(p.ParameterType))
            .ToArray();

        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(Target(Page(0)), args));
        Assert.IsType<NotImplementedException>(ex.InnerException);
    }

    /// <summary>And the four that have moved really do read through the context surface.</summary>
    /// <remarks>
    /// Driven through <see cref="IDatabase"/> rather than the group, because the point is the transitional
    /// adapter: the synchronous signatures block on each page using this type's own <c>Wait</c>, and
    /// nothing on the new surface blocks.
    /// </remarks>
    [Fact]
    public void TheSynchronousScansWalkTheCursor()
    {
        Assert.Equal(
            ["a", "b", "c"],
            Target(Page(7, "a", "b"), Page(0, "c")).SetScan("k").Select(v => v.ToString()));

        Assert.Equal(
            ["f1=v1", "f2=v2"],
            Target(Page(4, "f1", "v1"), Page(0, "f2", "v2")).HashScan("k").Select(e => $"{e.Name}={e.Value}"));

        Assert.Equal(
            ["f1", "f2"],
            Target(Page(4, "f1"), Page(0, "f2")).HashScanNoValues("k").Select(v => v.ToString()));

        Assert.Equal(
            ["m1:1.5", "m2:2"],
            Target(Page(3, "m1", "1.5"), Page(0, "m2", "2")).SortedSetScan("k").Select(e => $"{e.Element}:{e.Score}"));
    }

    [Fact]
    public async Task TheAsynchronousScansWalkTheCursor()
    {
        var seen = new List<string>();
        await foreach (var item in Target(Page(7, "a"), Page(0, "b")).SetScanAsync("k")) seen.Add(item.ToString());
        Assert.Equal(["a", "b"], seen);

        var fields = new List<string>();
        await foreach (var entry in Target(Page(4, "f1", "v1"), Page(0, "f2", "v2")).HashScanAsync("k"))
        {
            fields.Add($"{entry.Name}={entry.Value}");
        }

        Assert.Equal(["f1=v1", "f2=v2"], fields);
    }
}
