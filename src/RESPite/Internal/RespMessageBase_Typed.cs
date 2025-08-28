using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using RESPite.Messages;

namespace RESPite.Internal;

internal sealed class RespMessage<TResponse> : RespMessageBase<TResponse>
{
    private IRespParser<TResponse> _parser;

    private RespMessage()
    {
        Unsafe.SkipInit(out _parser);
    }

    private RespMessageBase<TResponse> Init(IRespParser<TResponse> parser)
    {
        _parser = parser;
        return InitParser(parser);
    }

    protected override TResponse Parse(ref RespReader reader) => _parser.Parse(ref reader);

    public override void Reset()
    {
        _parser = null!;
        base.Reset();
    }
}
