using System.Runtime.CompilerServices;
using RESPite.Messages;

namespace RESPite.Internal;

internal sealed class RespMessage<TState, TResponse> : RespMessageBase<TResponse>
{
    private TState _state;
    private IRespParser<TState, TResponse> _parser;

    private RespMessage()
    {
        Unsafe.SkipInit(out _state);
        Unsafe.SkipInit(out _parser);
    }

    protected override TResponse Parse(ref RespReader reader) => _parser.Parse(in _state, ref reader);

    private RespMessageBase<TResponse> Init(TState state, IRespParser<TState, TResponse> parser)
    {
        _state = state;
        _parser = parser;
        return InitParser(parser);
    }

    public override void Reset()
    {
        _state = default!;
        _parser = null!;
        base.Reset();
    }
}
