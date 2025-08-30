using RESPite.Messages;

namespace RESPite.Internal;

internal sealed class RespMessage<TResponse> : RespMessageBase<TResponse>
{
    private IRespParser<TResponse>? _parser;
    [ThreadStatic]
    // used for object recycling of the async machinery
    private static RespMessage<TResponse>? _threadStaticSpare;

    internal static RespMessage<TResponse> Get(IRespParser<TResponse>? parser)
    {
        RespMessage<TResponse> obj = _threadStaticSpare ?? new();
        _threadStaticSpare = null;
        obj._parser = parser;
        obj.InitParser(parser);
        return obj;
    }

    protected override void Recycle() => _threadStaticSpare = this;

    private RespMessage() { }

    protected override TResponse Parse(ref RespReader reader) => _parser!.Parse(ref reader);

    public override void Reset(bool recycle)
    {
        _parser = null!;
        base.Reset(recycle);
    }
}
