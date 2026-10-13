using System.Linq;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for SCRIPT LOAD's reply.
/// SCRIPT LOAD returns a bulk string containing the SHA1 hash (40 hex characters).
/// </summary>
/// <remarks>
/// The shipped <c>ScriptLoadProcessor</c> did read-and-convert in one step; the new core reads the text
/// with <see cref="RespHandlers.String"/> (<c>Scripts.LoadHex</c>) and <c>IServer.ScriptLoad</c> converts it
/// with <c>Scripts.Sha1Bytes</c>, refusing anything that is not a hash. <see cref="Load"/> is that pair.
/// The other half of the shipped processor - recording the script as loaded on the server - stays in
/// <c>RedisServer.ScriptLoadCore</c>, which needs a server.
/// </remarks>
public class ScriptLoad(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    private static bool TryLoad(string resp, out byte[]? hash)
    {
        hash = TryExecute(resp, RespHandlers.String, out var hex, out _) ? Scripts.Sha1Bytes(hex) : null;
        return hash is not null;
    }

    private static byte[] Load(string resp)
    {
        Assert.True(TryLoad(resp, out var hash));
        return hash!;
    }

    [Theory]
    [InlineData("$40\r\n829c3804401b0727f70f73d4415e162400cbe57b\r\n", "829c3804401b0727f70f73d4415e162400cbe57b")]
    [InlineData("$40\r\n0000000000000000000000000000000000000000\r\n", "0000000000000000000000000000000000000000")]
    [InlineData("$40\r\nffffffffffffffffffffffffffffffffffffffff\r\n", "ffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("$40\r\nABCDEF1234567890abcdef1234567890ABCDEF12\r\n", "ABCDEF1234567890abcdef1234567890ABCDEF12")]
    public void ScriptLoadProcessor_ValidHash(string resp, string expectedAsciiHash)
    {
        var result = Load(resp);

        Assert.NotNull(result);
        Assert.Equal(20, result.Length); // SHA1 is 20 bytes

        // Convert the byte array back to hex string to verify
        var actualHex = string.Concat(result.Select(b => b.ToString("x2")));
        Assert.Equal(expectedAsciiHash.ToLowerInvariant(), actualHex);
    }

    [Theory]
    [InlineData("$41\r\n829c3804401b0727f70f73d4415e162400cbe57bb\r\n")] // 41 chars instead of 40
    [InlineData("$0\r\n\r\n")] // empty string
    [InlineData("$-1\r\n")] // null bulk string
    [InlineData(":42\r\n")] // integer instead of bulk string
    [InlineData("+OK\r\n")] // simple string instead of bulk string
    [InlineData("*1\r\n$40\r\n829c3804401b0727f70f73d4415e162400cbe57b\r\n")] // array instead of bulk string
    public void ScriptLoadProcessor_InvalidFormat(string resp)
    {
        Assert.False(TryLoad(resp, out _));
    }

    [Theory]
    [InlineData("$40\r\n829c3804401b0727f70f73d4415e162400cbe5zz\r\n")] // invalid hex chars (zz)
    [InlineData("$40\r\n829c3804401b0727f70f73d4415e162400cbe5!!\r\n")] // invalid hex chars (!!)
    [InlineData("$40\r\n829c3804401b0727f70f73d4415e162400cbe5  \r\n")] // spaces instead of hex
    public void ScriptLoadProcessor_InvalidHexCharacters(string resp)
    {
        Assert.False(TryLoad(resp, out _));
    }
}
