using System;
using System.Buffers;
using System.Text;
using RESPite.Messages;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ResultProcessorUnitTests(ITestOutputHelper log)
{
    public void Log(string message) => log?.WriteLine(message);

    private protected static Message DummyMessage<T>()
        => Message.Create(0, default, RedisCommand.UNKNOWN);

    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    public void Int32(string resp, int value)
    {
        var result = Execute(resp, ResultProcessor.Int32);
        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    public void Int64(string resp, int value)
    {
        var result = Execute(resp, ResultProcessor.Int32);
        Assert.Equal(value, result);
    }

    private protected static T? Execute<T>(string resp, ResultProcessor<T> processor)
    {
        Assert.True(TryExecute<T>(resp, processor, out var value, out var ex));
        Assert.Null(ex);
        return value;
    }

    private protected static bool TryExecute<T>(string resp, ResultProcessor<T> processor, out T? value, out Exception? exception)
    {
        byte[]? lease = null;
        try
        {
            var maxLen = Encoding.UTF8.GetMaxByteCount(resp.Length);
            const int MAX_STACK = 128;
            Span<byte> oversized = maxLen <= MAX_STACK
                ? stackalloc byte[MAX_STACK]
                : (lease = ArrayPool<byte>.Shared.Rent(maxLen));

            var msg = DummyMessage<int>();
            var box = SimpleResultBox<T>.Get();
            msg.SetSource(processor, box);

            var reader = new RespReader(oversized.Slice(0, Encoding.UTF8.GetBytes(resp, oversized)));
            bool success = processor.SetResult(null!, msg, ref reader);
            exception = null;
            value = success ? box.GetResult(out exception, canRecycle: true) : default;
            return success;
        }
        finally
        {
            if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        }
    }
}
