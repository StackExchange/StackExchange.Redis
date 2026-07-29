using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Verifies the exact wire bytes of the individual <c>HIMPORT</c> messages. The field-set name is written as an
/// 8-byte bulk string (the token's opaque id); it is derived from the live token here rather than hard-coded, since
/// the id is a process-wide monotonic counter.
/// </summary>
public class HashImport(ITestOutputHelper log)
{
    // RESP bulk-string encoding of an arbitrary byte payload (may contain non-printable bytes).
    private static string Bulk(byte[] bytes)
    {
        var sb = new StringBuilder().Append('$').Append(bytes.Length).Append("\r\n");
        foreach (var b in bytes) sb.Append((char)b);
        return sb.Append("\r\n").ToString();
    }

    [Fact(Timeout = 5000)]
    public async Task Prepare_RoundTrips()
    {
        var token = StackExchange.Redis.HashImport.Create("name", "email", "age");
        byte[] name = BitConverter.GetBytes(token.Id);
        var msg = new HashImportPrepareMessage(0, CommandFlags.None, token);

        // HIMPORT PREPARE <field-set> name email age
        var request = "*6\r\n$7\r\nHIMPORT\r\n$7\r\nPREPARE\r\n" + Bulk(name) + "$4\r\nname\r\n$5\r\nemail\r\n$3\r\nage\r\n";
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.DemandOK, request, "+OK\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Set_RoundTrips()
    {
        var token = StackExchange.Redis.HashImport.Create("f1", "f2");
        byte[] name = BitConverter.GetBytes(token.Id);
        ReadOnlyMemory<RedisValue> values = new RedisValue[] { "v1", "v2" };
        var msg = new HashImportSetMessage(0, CommandFlags.None, token, (RedisKey)"user:1", values);

        // HIMPORT SET user:1 <field-set> v1 v2
        var request = "*6\r\n$7\r\nHIMPORT\r\n$3\r\nSET\r\n$6\r\nuser:1\r\n" + Bulk(name) + "$2\r\nv1\r\n$2\r\nv2\r\n";
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.DemandOK, request, "+OK\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Set_SingleValue_RoundTrips()
    {
        var token = StackExchange.Redis.HashImport.Create("only");
        byte[] name = BitConverter.GetBytes(token.Id);
        ReadOnlyMemory<RedisValue> values = new RedisValue[] { "v" };
        var msg = new HashImportSetMessage(0, CommandFlags.None, token, (RedisKey)"k", values);

        var request = "*5\r\n$7\r\nHIMPORT\r\n$3\r\nSET\r\n$1\r\nk\r\n" + Bulk(name) + "$1\r\nv\r\n";
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.DemandOK, request, "+OK\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Discard_RoundTrips()
    {
        var token = StackExchange.Redis.HashImport.Create("f");
        byte[] name = BitConverter.GetBytes(token.Id);
        var msg = new HashImportDiscardMessage(0, CommandFlags.None, token);

        // HIMPORT DISCARD <field-set>
        var request = "*3\r\n$7\r\nHIMPORT\r\n$7\r\nDISCARD\r\n" + Bulk(name);
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.DemandOK, request, "+OK\r\n", log: log);
        Assert.True(result);
    }
}
