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
    private const string ATTRIB_FOO_BAR = "|1\r\n+foo\r\n+bar\r\n";

    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    [InlineData(",1\r\n", 1)]
    [InlineData(ATTRIB_FOO_BAR + ":1\r\n", 1)]
    [InlineData(":-42\r\n", -42)]
    [InlineData("+-42\r\n", -42)]
    [InlineData("$3\r\n-42\r\n", -42)]
    [InlineData(",-42\r\n", -42)]
    public void Int32(string resp, int value) => Assert.Equal(value, Execute(resp, ResultProcessor.Int32));

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$4\r\nPONG\r\n")]
    public void FailingInt32(string resp) => ExecuteUnexpected(resp, ResultProcessor.Int32);

    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    [InlineData(",1\r\n", 1)]
    [InlineData(ATTRIB_FOO_BAR + ":1\r\n", 1)]
    [InlineData(":-42\r\n", -42)]
    [InlineData("+-42\r\n", -42)]
    [InlineData("$3\r\n-42\r\n", -42)]
    [InlineData(",-42\r\n", -42)]
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
    [InlineData(ATTRIB_FOO_BAR + "*1\r\n+42\r\n", "42")]
    public void Int64Array(string resp, string? value) => Assert.Equal(value, Join(Execute(resp, ResultProcessor.Int64Array)));

    [Theory]
    [InlineData(":42\r\n", 42.0)]
    [InlineData("+3.14\r\n", 3.14)]
    [InlineData("$4\r\n3.14\r\n", 3.14)]
    [InlineData(",3.14\r\n", 3.14)]
    [InlineData(ATTRIB_FOO_BAR + ",3.14\r\n", 3.14)]
    [InlineData(":-1\r\n", -1.0)]
    [InlineData("+inf\r\n", double.PositiveInfinity)]
    [InlineData(",inf\r\n", double.PositiveInfinity)]
    [InlineData("$4\r\n-inf\r\n", double.NegativeInfinity)]
    [InlineData(",-inf\r\n", double.NegativeInfinity)]
    [InlineData(",nan\r\n", double.NaN)]
    public void Double(string resp, double value) => Assert.Equal(value, Execute(resp, ResultProcessor.Double));

    [Theory]
    [InlineData("_\r\n", null)]
    [InlineData("$-1\r\n", null)]
    [InlineData(":42\r\n", 42L)]
    [InlineData("+42\r\n", 42L)]
    [InlineData("$2\r\n42\r\n", 42L)]
    [InlineData(",42\r\n", 42L)]
    [InlineData(ATTRIB_FOO_BAR + ":42\r\n", 42L)]
    public void NullableInt64(string resp, long? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableInt64));

    [Theory]
    [InlineData("*1\r\n:99\r\n", 99L)]
    [InlineData(ATTRIB_FOO_BAR + "*1\r\n:99\r\n", 99L)]
    public void NullableInt64ArrayOfOne(string resp, long? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableInt64));

    [Theory]
    [InlineData("*-1\r\n")] // null array
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n:1\r\n:2\r\n")] // two elements
    public void FailingNullableInt64ArrayOfNonOne(string resp) => ExecuteUnexpected(resp, ResultProcessor.NullableInt64);

    [Theory]
    [InlineData("_\r\n", null)]
    [InlineData("$-1\r\n", null)]
    [InlineData(":42\r\n", 42.0)]
    [InlineData("+3.14\r\n", 3.14)]
    [InlineData("$4\r\n3.14\r\n", 3.14)]
    [InlineData(",3.14\r\n", 3.14)]
    [InlineData(ATTRIB_FOO_BAR + ",3.14\r\n", 3.14)]
    public void NullableDouble(string resp, double? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableDouble));

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
