using RESPite.Messages;

namespace RESPite.Internal;

internal sealed class RespStatelessMessage<TResponse> : RespMessageBase<TResponse>
{
    private IRespParser<TResponse>? _parser;
    [ThreadStatic]
    // used for object recycling of the async machinery
    private static RespStatelessMessage<TResponse>? _threadStaticSpare;

    internal static RespStatelessMessage<TResponse> Get(IRespParser<TResponse>? parser)
    {
        RespStatelessMessage<TResponse> obj = _threadStaticSpare ?? new();
        _threadStaticSpare = null;
        obj._parser = parser;
        obj.InitParser(parser);
        return obj;
    }

    protected override void Recycle() => _threadStaticSpare = this;

    private RespStatelessMessage() { }

    protected override TResponse Parse(ref RespReader reader) => _parser!.Parse(ref reader);

    public override void Reset(bool recycle)
    {
        _parser = null!;
        base.Reset(recycle);
    }
}
