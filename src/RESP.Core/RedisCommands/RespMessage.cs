using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resp.RedisCommands;

public class RespMessage
{
    public static RespMessageBuilder<T> Create<T>(ReadOnlySpan<byte> command, T value, IRespFormatter<T>? formatter = null)
        => new(command, value, formatter ?? DefaultFormatters.Get<T>());
}

public readonly ref struct RespMessageBuilder<TRequest>(ReadOnlySpan<byte> command, TRequest value, IRespFormatter<TRequest> formatter)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
{
    private readonly ReadOnlySpan<byte> _command = command;
    private readonly TRequest _value = value;
    public TResponse Wait<TResponse>(IRespConnection connection, IRespParser<TResponse>? parser = null, TimeSpan timeout = default)
        => connection.Send(_command, _value, formatter, parser, timeout);
    public void Wait(IRespConnection connection, IRespParser<Void>? parser = null, TimeSpan timeout = default)
        => connection.Send(_command, _value, formatter, parser, timeout);
    public Task<T> WaitAsync<T>(IRespConnection connection, IRespParser<T>? parser = null, CancellationToken cancellationToken = default)
        => connection.SendAsync(_command, _value, formatter, parser, cancellationToken);
    public Task WaitAsync(IRespConnection connection, IRespParser<Void>? parser = null, CancellationToken cancellationToken = default)
        => connection.SendAsync(_command, _value, formatter, parser, cancellationToken);
}
