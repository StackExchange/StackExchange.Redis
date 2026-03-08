using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StackExchange.Redis.Server;

public partial class RedisClient
{
    private static readonly UnboundedChannelOptions s_replyChannelOptions = new()
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    };

    private readonly Channel<TypedRedisValue> _replies = Channel.CreateUnbounded<TypedRedisValue>(s_replyChannelOptions);

    public void AddOutbound(in TypedRedisValue message)
    {
        if (message.IsNil)
        {
            message.Recycle();
            return;
        }

        try
        {
            if (!_replies.Writer.TryWrite(message))
            {
                // sorry, we're going to need it, but in reality: we're using
                // unbounded channels, so this isn't an issue
                _replies.Writer.WriteAsync(message).AsTask().Wait();
            }
        }
        catch
        {
            message.Recycle();
        }
    }
    public ValueTask AddOutboundAsync(in TypedRedisValue message, CancellationToken cancellationToken = default)
    {
        if (message.IsNil)
        {
            message.Recycle();
            return default;
        }
        return _replies.Writer.WriteAsync(message, cancellationToken);
    }

    public void Complete(Exception ex = null) => _replies.Writer.TryComplete(ex);

    public async Task WriteOutputAsync(PipeWriter writer, CancellationToken cancellationToken = default)
    {
        try
        {
            var reader = _replies.Reader;
            do
            {
                while (reader.TryRead(out var message))
                {
                    await RespServer.WriteResponseAsync(this, writer, message, Protocol);
                    message.Recycle();
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            // await more data
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false));
            await writer.CompleteAsync();
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex);
        }
    }
}
