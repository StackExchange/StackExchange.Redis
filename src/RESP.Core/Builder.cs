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
    public TResponse Wait<TResponse>(IRespParser<Void, TResponse> parser)
        => context.Send(_command, _value, formatter, parser);

    public void Wait()
        => context.Send(_command, _value, formatter, RespParsers.Success);
    public void Wait(IRespParser<Void, Void> parser)
        => context.Send(_command, _value, formatter, parser);

    public Task<TResponse> AsTask<TResponse>()
        => context.SendTaskAsync(_command, _value, formatter, RespParsers.Get<TResponse>());
    public Task<TResponse> AsTask<TResponse>(IRespParser<Void, TResponse> parser)
        => context.SendTaskAsync(_command, _value, formatter, parser);

    public Task AsTask()
        => context.SendTaskAsync(_command, _value, formatter, RespParsers.Success);
    public Task AsTask(IRespParser<Void, Void> parser)
        => context.SendTaskAsync(_command, _value, formatter, parser);

    public ValueTask<TResponse> AsValueTask<TResponse>()
        => context.SendValueTaskAsync(_command, _value, formatter, RespParsers.Get<TResponse>());
    public ValueTask<TResponse> AsValueTask<TResponse>(IRespParser<Void, TResponse> parser)
        => context.SendValueTaskAsync(_command, _value, formatter, parser);

    public ValueTask AsValueTask()
        => context.SendValueTaskAsync(_command, _value, formatter, RespParsers.Success);
    public ValueTask AsValueTask(IRespParser<Void, Void> parser)
        => context.SendValueTaskAsync(_command, _value, formatter, parser);
}
