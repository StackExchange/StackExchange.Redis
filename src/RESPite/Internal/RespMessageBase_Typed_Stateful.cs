namespace RESPite.Internal;

internal sealed class RespMessage<TState, TResponse> : RespMessageBaseT<TResponse>
{
    private TState _state;
    private IRespParser<TState, TResponse>? _parser;
}
