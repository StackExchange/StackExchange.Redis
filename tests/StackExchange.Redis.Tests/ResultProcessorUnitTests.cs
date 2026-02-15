using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite.Messages;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ResultProcessorUnitTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    [InlineData(":-42\r\n", -42)]
    [InlineData("+-42\r\n", -42)]
    [InlineData("$3\r\n-42\r\n", -42)]
    public void Int32(string resp, int value) => Assert.Equal(value, Execute(resp, ResultProcessor.Int32));

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$4\r\nPONG\r\n")]
    public void FailingInt32(string resp) => ExecuteUnexpected(resp, ResultProcessor.Int32);

    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    [InlineData(":-42\r\n", -42)]
    [InlineData("+-42\r\n", -42)]
    [InlineData("$3\r\n-42\r\n", -42)]
    public void Int64(string resp, long value) => Assert.Equal(value, Execute(resp, ResultProcessor.Int64));

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$4\r\nPONG\r\n")]
    public void FailingInt64(string resp) => ExecuteUnexpected(resp, ResultProcessor.Int64);

    [Theory]
    [InlineData("*-1\r\n", null)]
    [InlineData("*0\r\n", "")]
    [InlineData("*1\r\n+42\r\n", "42")]
    [InlineData("*2\r\n+42\r\n:78\r\n", "42,78")]
    public void Int64Array(string resp, string? value) => Assert.Equal(value, Join(Execute(resp, ResultProcessor.Int64Array)));

    [return: NotNullIfNotNull(nameof(array))]
    protected static string? Join<T>(T[]? array, string separator = ",")
    {
        if (array is null) return null;
        return string.Join(separator, array);
    }

    public void Log(string message) => log?.WriteLine(message);

    private protected static Message DummyMessage<T>()
        => Message.Create(0, default, RedisCommand.UNKNOWN);

    private protected void ExecuteUnexpected<T>(
        string resp,
        ResultProcessor<T> processor,
        ConnectionType connectionType = ConnectionType.Interactive,
        RedisProtocol protocol = RedisProtocol.Resp2,
        [CallerMemberName] string caller = "")
    {
        Assert.False(TryExecute(resp, processor, out _, out var ex));
        if (ex is not null) Log(ex.Message);
        Assert.StartsWith("Unexpected response to UNKNOWN:", Assert.IsType<RedisConnectionException>(ex).Message);
    }
    private protected static T? Execute<T>(
        string resp,
        ResultProcessor<T> processor,
        ConnectionType connectionType = ConnectionType.Interactive,
        RedisProtocol protocol = RedisProtocol.Resp2,
        [CallerMemberName] string caller = "")
    {
        Assert.True(TryExecute<T>(resp, processor, out var value, out var ex));
        Assert.Null(ex);
        return value;
    }

    private protected static bool TryExecute<T>(
        string resp,
        ResultProcessor<T> processor,
        out T? value,
        out Exception? exception,
        ConnectionType connectionType = ConnectionType.Interactive,
        RedisProtocol protocol = RedisProtocol.Resp2,
        [CallerMemberName] string caller = "")
    {
        byte[]? lease = null;
        try
        {
            var maxLen = Encoding.UTF8.GetMaxByteCount(resp.Length);
            const int MAX_STACK = 128;
            Span<byte> oversized = maxLen <= MAX_STACK
                ? stackalloc byte[MAX_STACK]
                : (lease = ArrayPool<byte>.Shared.Rent(maxLen));

            var msg = DummyMessage<T>();
            var box = SimpleResultBox<T>.Get();
            msg.SetSource(processor, box);

            var reader = new RespReader(oversized.Slice(0, Encoding.UTF8.GetBytes(resp, oversized)));
            PhysicalConnection connection = new(connectionType, protocol, caller);
            Assert.True(processor.SetResult(connection, msg, ref reader));
            value = box.GetResult(out exception, canRecycle: true);
            return exception is null;
        }
        finally
        {
            if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        }
    }
}
