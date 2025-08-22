using System;
using System.Threading.Tasks;

namespace Resp;

public readonly ref struct RespMessageBuilder<TRequest>(RespContext context, ReadOnlySpan<byte> command, TRequest value, IRespFormatter<TRequest> formatter)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
{
    private readonly ReadOnlySpan<byte> _command = command;
    private readonly TRequest _value = value; // cannot inline to .ctor because of "allows ref struct"

    public TResponse Wait<TResponse>()
        => context.Send(_command, _value, formatter, RespParsers.Get<TResponse>());
    public TResponse Wait<TResponse>(IRespParser<TResponse> parser)
        => context.Send(_command, _value, formatter, parser);

    public void Wait()
        => context.Send(_command, _value, formatter, RespParsers.Success);
    public void Wait(IRespParser<Void> parser)
        => context.Send(_command, _value, formatter, parser);

    public Task<TResponse> WaitAsync<TResponse>()
        => context.SendAsync(_command, _value, formatter, RespParsers.Get<TResponse>());
    public Task<TResponse> WaitAsync<TResponse>(IRespParser<TResponse> parser)
        => context.SendAsync(_command, _value, formatter, parser);

    public Task WaitAsync()
        => context.SendAsync(_command, _value, formatter, RespParsers.Success);
    public Task WaitAsync(IRespParser<Void> parser)
        => context.SendAsync(_command, _value, formatter, parser);
}
