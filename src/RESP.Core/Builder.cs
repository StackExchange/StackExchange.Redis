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
        => Message.Send(context, _command, _value, formatter, RespParsers.Get<TResponse>());
    public TResponse Wait<TResponse>(IRespParser<Void, TResponse> parser)
        => Message.Send(context, _command, _value, formatter, parser);

    public void Wait()
        => Message.Send(context, _command, _value, formatter, RespParsers.Success);
    public void Wait(IRespParser<Void, Void> parser)
        => Message.Send(context, _command, _value, formatter, parser);

    public ValueTask<TResponse> AsValueTask<TResponse>()
        => Message.SendAsync(context, _command, _value, formatter, RespParsers.Get<TResponse>());
    public ValueTask<TResponse> AsValueTask<TResponse>(IRespParser<Void, TResponse> parser)
        => Message.SendAsync(context, _command, _value, formatter, parser);

    public ValueTask AsValueTask()
        => Message.SendAsync(context, _command, _value, formatter, RespParsers.Success);
    public ValueTask AsValueTask(IRespParser<Void, Void> parser)
        => Message.SendAsync(context, _command, _value, formatter, parser);
}
