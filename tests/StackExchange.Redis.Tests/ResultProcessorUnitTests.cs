using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite.Messages;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Unit tests for ResultProcessor implementations.
/// Tests are organized into partial class files by category.
/// </summary>
public partial class ResultProcessorUnitTests(ITestOutputHelper log)
{
    private protected const string ATTRIB_FOO_BAR = "|1\r\n+foo\r\n+bar\r\n";
    private protected static readonly ResultProcessor.Int64DefaultValueProcessor Int64DefaultValue999 = new(999);

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
        Assert.False(TryExecute(resp, processor, out _, out var ex, connectionType, protocol, caller), caller);
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
        Assert.True(TryExecute<T>(resp, processor, out var value, out var ex, connectionType, protocol, caller));
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
            PhysicalConnection connection = new(connectionType, protocol, name: caller);
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
