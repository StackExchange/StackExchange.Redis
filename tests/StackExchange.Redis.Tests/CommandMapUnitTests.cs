using System;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Pure unit tests (no server) over the default <see cref="CommandMap"/>, pinning the exact RESP
/// bulk-string chunk that would be written to the wire for a given <see cref="RedisCommand"/>.
/// </summary>
public class CommandMapUnitTests
{
    [Theory]
    // a vanilla command, for baseline
    [InlineData(RedisCommand.GET, "$3\r\nGET\r\n")]
    [InlineData(RedisCommand.ZREMRANGEBYSCORE, "$16\r\nZREMRANGEBYSCORE\r\n")]
    // the read-only variants: the wire name uses an UNDERSCORE (these are the real Redis command
    // names: EVAL_RO / EVALSHA_RO / SORT_RO), which is what command.ToString() yields today.
    [InlineData(RedisCommand.EVAL_RO, "$7\r\nEVAL_RO\r\n")]
    [InlineData(RedisCommand.EVALSHA_RO, "$10\r\nEVALSHA_RO\r\n")]
    [InlineData(RedisCommand.SORT_RO, "$7\r\nSORT_RO\r\n")]
    public void DefaultCommandMap_GetResp_ProducesExpectedWireBytes(object command, string expectedResp)
    {
        // command is boxed as object because RedisCommand is internal (less accessible than this public method)
        ReadOnlySpan<byte> resp = CommandMap.Default.GetResp((RedisCommand)command);
        Assert.Equal(expectedResp, Encoding.ASCII.GetString(resp));
    }

    [Theory]
    // vanilla command, for baseline
    [InlineData("GET", RedisCommand.GET)]
    [InlineData("ZREMRANGEBYSCORE", RedisCommand.ZREMRANGEBYSCORE)]
    // the underscore variants: parsing the real Redis wire name (with an underscore) MUST round-trip
    // back to the matching enum value. This guards against the AsciiHash code-gen inferring '_' -> '-'
    // (which would only recognise "EVAL-RO" and fail to parse the actual "EVAL_RO").
    [InlineData("EVAL_RO", RedisCommand.EVAL_RO)]
    [InlineData("EVALSHA_RO", RedisCommand.EVALSHA_RO)]
    [InlineData("SORT_RO", RedisCommand.SORT_RO)]
    public void TryParseCI_ParsesRealWireName(string name, object expected)
    {
        var expectedCommand = (RedisCommand)expected;

        Assert.True(RedisCommandMetadata.TryParseCI(name.AsSpan(), out var fromChars), $"char parse failed for '{name}'");
        Assert.Equal(expectedCommand, fromChars);

        ReadOnlySpan<byte> bytes = Encoding.ASCII.GetBytes(name);
        Assert.True(RedisCommandMetadata.TryParseCI(bytes, out var fromBytes), $"byte parse failed for '{name}'");
        Assert.Equal(expectedCommand, fromBytes);
    }

    [Theory]
    // NONE is a client-side sentinel, not a command, so it carries an explicit empty AsciiHash
    // token and must not be parsable in any casing
    [InlineData("NONE")]
    [InlineData("none")]
    [InlineData("None")]
    // and something that was never a command, for baseline
    [InlineData("NOT_A_COMMAND")]
    public void TryParseCI_RejectsNonCommands(string name)
    {
        Assert.False(RedisCommandMetadata.TryParseCI(name.AsSpan(), out _), $"char parse unexpectedly succeeded for '{name}'");

        ReadOnlySpan<byte> bytes = Encoding.ASCII.GetBytes(name);
        Assert.False(RedisCommandMetadata.TryParseCI(bytes, out _), $"byte parse unexpectedly succeeded for '{name}'");
    }

    /// <summary>
    /// We now issue <c>HELLO</c> whenever it is available (RESP2 included), so the proxy maps must exclude it:
    /// twemproxy 0.5.0 *closes the connection* on an unsupported command, and envoy (1.39, at least) forwards
    /// HELLO to an arbitrary backend node, so its version/role/mode describe the wrong server.
    /// </summary>
    [Fact]
    public void ProxyCommandMapsExcludeHello()
    {
        Assert.False(CommandMap.Twemproxy.IsAvailable(RedisCommand.HELLO), "twemproxy");
        Assert.False(CommandMap.Envoyproxy.IsAvailable(RedisCommand.HELLO), "envoyproxy");
        Assert.True(CommandMap.Default.IsAvailable(RedisCommand.HELLO), "default");
    }
}
