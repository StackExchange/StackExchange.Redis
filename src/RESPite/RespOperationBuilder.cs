using RESPite.Messages;

namespace RESPite;

public readonly ref struct RespOperationBuilder<TRequest>(
    in RespContext context,
    ReadOnlySpan<byte> command,
    TRequest value,
    IRespFormatter<TRequest> formatter)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
{
    private readonly RespContext _context = context;
    private readonly ReadOnlySpan<byte> _command = command;
    private readonly TRequest _value = value; // cannot inline to .ctor because of "allows ref struct"

    public TResponse Wait<TResponse>()
        => Send(RespParsers.Get<TResponse>()).Wait();

    public TResponse Wait<TResponse>(IRespParser<TResponse> parser)
        => Send(parser).Wait();

    public TResponse Wait<TState, TResponse>(in TState state)
        => Send(in state, RespParsers.Get<TState, TResponse>()).Wait();

    public TResponse Wait<TState, TResponse>(in TState state, IRespParser<TState, TResponse> parser)
        => Send(in state, parser).Wait();

    public void Wait() => Send(RespParsers.Success).Wait();

    public RespOperation<TResponse> Send<TResponse>()
        => Send(RespParsers.Get<TResponse>());

    public RespOperation<TResponse> Send<TResponse>(IRespParser<TResponse> parser)
    {
        _ = _context;
        _ = formatter;
        throw new NotImplementedException();
    }

    public RespOperation Send() => Send(RespParsers.Success);
    public RespOperation<TResponse> Send<TState, TResponse>(in TState state)
        => Send(in state, RespParsers.Get<TState, TResponse>());

    public RespOperation<TResponse> Send<TState, TResponse>(in TState state, IRespParser<TState, TResponse> parser)
    {
        throw new NotImplementedException();
    }
}
