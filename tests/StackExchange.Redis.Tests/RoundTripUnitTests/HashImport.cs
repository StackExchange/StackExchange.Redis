using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Verifies the exact wire bytes of the individual <c>HIMPORT</c> sub-messages that
/// <see cref="IDatabase.HashImport"/> unrolls into. The fieldset GUID is written as a 16-byte bulk string;
/// <see cref="Guid.Empty"/> gives a deterministic run of 16 zero bytes to assert against.
/// </summary>
public class HashImport(ITestOutputHelper log)
{
    private static readonly string FieldSet = "$16\r\n" + new string('\0', 16) + "\r\n";

    [Fact(Timeout = 5000)]
    public async Task Prepare_RoundTrips()
    {
        ReadOnlyMemory<RedisValue> fields = new RedisValue[] { "name", "email", "age" };
        var msg = new RedisDatabase.HashImportPrepareMessage(0, CommandFlags.None, Guid.Empty, fields);

        // HIMPORT PREPARE <fieldset> name email age
        var request = "*6\r\n$7\r\nHIMPORT\r\n$7\r\nPREPARE\r\n" + FieldSet + "$4\r\nname\r\n$5\r\nemail\r\n$3\r\nage\r\n";
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.DemandOK, request, "+OK\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Set_RoundTrips()
    {
        ReadOnlyMemory<RedisValue> values = new RedisValue[] { "v1", "v2" };
        var msg = new RedisDatabase.HashImportSetMessage(0, CommandFlags.None, Guid.Empty, (RedisKey)"user:1", 0, values);

        // HIMPORT SET user:1 <fieldset> v1 v2  (key is arg index 2 per the server key-spec)
        var request = "*6\r\n$7\r\nHIMPORT\r\n$3\r\nSET\r\n$6\r\nuser:1\r\n" + FieldSet + "$2\r\nv1\r\n$2\r\nv2\r\n";
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.DemandOK, request, "+OK\r\n", log: log);
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Set_SingleValue_RoundTrips()
    {
        ReadOnlyMemory<RedisValue> values = new RedisValue[] { "only" };
        var msg = new RedisDatabase.HashImportSetMessage(0, CommandFlags.None, Guid.Empty, (RedisKey)"k", 0, values);

        var request = "*5\r\n$7\r\nHIMPORT\r\n$3\r\nSET\r\n$1\r\nk\r\n" + FieldSet + "$4\r\nonly\r\n";
        var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.DemandOK, request, "+OK\r\n", log: log);
        Assert.True(result);
    }
}
