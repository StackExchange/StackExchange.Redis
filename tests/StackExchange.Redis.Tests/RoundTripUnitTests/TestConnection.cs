using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Tests;
using Xunit;

namespace StackExchange.Redis.Tests.RountTripUnitTests;

public static class CancellationTokenExtensions
{
    public static CancellationTokenRegistration CancelWithTest<T>(this TaskCompletionSource<T> tcs)
        => CancelWith(tcs, TestContext.Current.CancellationToken);
    public static CancellationTokenRegistration CancelWith<T>(this TaskCompletionSource<T> tcs, CancellationToken token)
    {
        if (token.CanBeCanceled)
        {
            token.ThrowIfCancellationRequested();
            return token.Register(() => tcs.TrySetCanceled(token)); // note capture for token: deal with it
        }

        return default;
    }
}
public class TestConnection : IDisposable
{
    internal static async Task<T> ExecuteAsync<T>(
        Message message,
        ResultProcessor<T> processor,
        string requestResp,
        string responseResp,
        ConnectionType connectionType = ConnectionType.Interactive,
        RedisProtocol protocol = RedisProtocol.Resp2,
        CommandMap? commandMap = null,
        byte[]? channelPrefix = null,
        [CallerMemberName] string caller = "")
    {
        // Validate RESP samples are not null/empty to avoid test setup mistakes
        Assert.False(string.IsNullOrEmpty(requestResp), "requestResp must not be null or empty");
        Assert.False(string.IsNullOrEmpty(responseResp), "responseResp must not be null or empty");

        using var conn = new TestConnection(false, connectionType, protocol, caller);

        var box = TaskResultBox<T>.Create(out var tcs, null);
        using var timeout = tcs.CancelWithTest();

        message.SetSource(box, processor);
        conn.WriteOutbound(message, commandMap, channelPrefix);
        Assert.Equal(TaskStatus.WaitingForActivation, tcs.Task.Status); // should be pending, since we haven't responded yet

        // check the request
        conn.AssertOutbound(requestResp);

        // since the request was good, we can start reading
        conn.StartReading();
        await conn.AddInboundAsync(responseResp);
        return await tcs.Task;
    }

    public TestConnection(
        bool startReading = true,
        ConnectionType connectionType = ConnectionType.Interactive,
        RedisProtocol protocol = RedisProtocol.Resp2,
        [CallerMemberName] string caller = "")
    {
        _physical = new PhysicalConnection(connectionType, protocol, _stream, caller);
        if (startReading) StartReading();
    }
    private readonly TestDuplexStream _stream = new();
    private readonly PhysicalConnection _physical;

    public void StartReading() => _physical.StartReading(TestContext.Current.CancellationToken);

    public ReadOnlySpan<byte> GetOutboundData() => _stream.GetOutboundData();
    public void FlushOutboundData() => _stream.FlushOutboundData();
    public ValueTask AddInboundAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _stream.AddInboundAsync(data, cancellationToken);
    public ValueTask AddInboundAsync(ReadOnlySpan<byte> data, CancellationToken cancellationToken = default)
        => _stream.AddInboundAsync(data, cancellationToken);
    public ValueTask AddInboundAsync(string data, CancellationToken cancellationToken = default)
        => _stream.AddInboundAsync(data, cancellationToken);

    public void Dispose()
    {
        _physical.Dispose();
        _stream.Dispose();
    }

    internal void WriteOutbound(Message message, CommandMap? commandMap = null, byte[]? channelPrefix = null)
    {
        _physical.EnqueueInsideWriteLock(message, enforceMuxer: false);
        message.WriteTo(_physical, commandMap ?? CommandMap.Default, channelPrefix);
    }

    public void AssertOutbound(string expected)
    {
        // Check max char count and lease a char buffer
        var actual = GetOutboundData();
        var maxCharCount = Encoding.UTF8.GetMaxCharCount(actual.Length);
        char[]? leased = null;
        Span<char> charBuffer = maxCharCount <= 256
            ? stackalloc char[maxCharCount]
            : (leased = ArrayPool<char>.Shared.Rent(maxCharCount));

        try
        {
            // Decode into the buffer
            var actualCharCount = Encoding.UTF8.GetChars(actual, charBuffer);
            var actualChars = charBuffer.Slice(0, actualCharCount);

            // Use SequenceEqual to compare
            if (actualChars.SequenceEqual(expected.AsSpan()))
            {
                return; // Success - no allocation needed
            }

            // Only if comparison fails: allocate string for useful error message
            var actualString = actualChars.ToString();
            Assert.Equal(expected, actualString);
        }
        finally
        {
            if (leased != null)
            {
                ArrayPool<char>.Shared.Return(leased);
            }
        }
    }
}
