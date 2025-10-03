using RESPite.Messages;

namespace RESPite;

public readonly ref struct RespOperationBuilder<TRequest>(
    in RespContext context,
    ReadOnlySpan<byte> command,
    TRequest request,
    IRespFormatter<TRequest> formatter)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
{
    private readonly RespContext _context = context;
    private readonly ReadOnlySpan<byte> _command = command;
    private readonly TRequest request = request; // cannot inline to .ctor because of "allows ref struct"

    public TResponse Wait<TResponse>()
        => Send(RespParsers.Get<TResponse>()).Wait(_context.SyncTimeout);

    public TResponse Wait<TResponse>(IRespParser<TResponse> parser)
        => Send(parser).Wait(_context.SyncTimeout);

    public TResponse Wait<TState, TResponse>(in TState state)
        => Send(in state, RespParsers.Get<TState, TResponse>()).Wait(_context.SyncTimeout);

    public TResponse Wait<TState, TResponse>(in TState state, IRespParser<TState, TResponse> parser)
        => Send(in state, parser).Wait(_context.SyncTimeout);

    public void Wait() => Send(RespParsers.Success).Wait(_context.SyncTimeout);

    public RespOperation<TResponse> Send<TResponse>()
        => _context.Send(_command, request, formatter, RespParsers.Get<TResponse>());

    public RespOperation<TResponse> Send<TResponse>(IRespParser<TResponse> parser)
        => _context.Send(_command, request, formatter, parser);

    public RespOperation Send() => _context.Send(_command, request, formatter, RespParsers.Success);
    public RespOperation<TResponse> Send<TState, TResponse>(in TState state)
        => _context.Send(_command, request, formatter, in state, RespParsers.Get<TState, TResponse>());

    public RespOperation<TResponse> Send<TState, TResponse>(in TState state, IRespParser<TState, TResponse> parser)
        => _context.Send(_command, request, formatter, in state, parser);
}
