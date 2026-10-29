using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// A <see cref="FakeExecutor"/> that also keeps each frame's raw bytes, for golden-RESP assertions.
/// </summary>
/// <remarks>
/// <see cref="FakeExecutor.Sent"/> decodes as UTF-8 and folds <c>CRLF</c> to <c>|</c>, which is the right
/// shape for most tests but cannot pin bytes that are not text (a <c>HIMPORT</c> field-set name is an
/// opaque 8-byte id).
/// </remarks>
internal sealed class RoundTripExecutor(params string[] replies) : FakeExecutor(replies)
{
    /// <summary>Every frame sent, byte for byte.</summary>
    public List<byte[]> Frames { get; } = [];

    protected override void OnSent(in RespRequest request) => Frames.Add(request.Span.ToArray());
}

/// <summary>
/// The new-core replacement for the old <c>TestConnection</c>: a command goes out through the real surface
/// (<see cref="IDatabase"/> over a <see cref="RespDatabaseContext"/>), its frame is captured as the
/// executor receives it, and a canned reply is parsed by the command's own handler.
/// </summary>
internal static class RoundTrip
{
    /// <summary>An <see cref="IDatabase"/> whose every command lands in <paramref name="executor"/>.</summary>
    public static IDatabase Database(RoundTripExecutor executor, CommandMap? commandMap = null)
        => Context(executor, commandMap).AsDatabase(Substitute.For<IConnectionMultiplexer>());

    /// <summary>The context surface over <paramref name="executor"/>.</summary>
    public static RespDatabaseContext Context(RoundTripExecutor executor, CommandMap? commandMap = null)
        => new(new RespContext(commandMap).WithExecutor(executor));

    /// <summary>
    /// Run <paramref name="command"/>, assert it wrote exactly <paramref name="requestResp"/>, and return
    /// what <paramref name="responseResp"/> parsed to.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<IDatabaseAsync, Task<T>> command,
        string requestResp,
        string responseResp,
        CommandMap? commandMap = null,
        ITestOutputHelper? log = null,
        RedisFeatures? features = null)
    {
        // Validate RESP samples are not null/empty to avoid test setup mistakes
        Assert.False(string.IsNullOrEmpty(responseResp), "responseResp must not be null or empty");

        var executor = new RoundTripExecutor(responseResp) { Features = features };
        var result = await command(Database(executor, commandMap));
        AssertSent(executor, log, requestResp);
        return result;
    }

    /// <summary>Assert the frames sent, in order, as RESP text.</summary>
    /// <remarks>
    /// Each byte maps to one <see cref="char"/> (Latin-1), so the comparison is exact for ASCII and still
    /// byte-exact for binary payloads built the same way.
    /// </remarks>
    public static void AssertSent(RoundTripExecutor executor, ITestOutputHelper? log, params string[] expected)
    {
        var actual = executor.Frames.Select(Latin1).ToArray();
        if (!expected.SequenceEqual(actual))
        {
            foreach (var frame in expected) log?.WriteLine("Expected: {0}", frame);
            foreach (var frame in actual) log?.WriteLine("Actual:   {0}", frame);
        }

        Assert.Equal(expected, actual);
    }

    private static string Latin1(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
        return new string(chars);
    }
}
