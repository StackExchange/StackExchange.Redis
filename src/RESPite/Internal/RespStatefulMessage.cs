using System.Runtime.CompilerServices;
using RESPite.Messages;

namespace RESPite.Internal;

internal sealed class RespStatefulMessage<TState, TResponse> : RespMessageBase<TResponse>
{
    private TState _state;
    private IRespParser<TState, TResponse>? _parser;
    [ThreadStatic]
    // used for object recycling of the async machinery
    private static RespStatefulMessage<TState, TResponse>? _threadStaticSpare;
    internal static RespStatefulMessage<TState, TResponse> Get(in TState state, IRespParser<TState, TResponse>? parser)
    {
        RespStatefulMessage<TState, TResponse> obj = _threadStaticSpare ?? new();
        _threadStaticSpare = null;
        obj._state = state;
        obj._parser = parser;
        obj.InitParser(parser);
        return obj;
    }

    protected override void Recycle() => _threadStaticSpare = this;

    private RespStatefulMessage() => Unsafe.SkipInit(out _state);

    protected override TResponse Parse(ref RespReader reader) => _parser!.Parse(in _state, ref reader);

    protected override void Reset(bool recycle)
    {
        _state = default!;
        _parser = null!;
        base.Reset(recycle);
    }
}
