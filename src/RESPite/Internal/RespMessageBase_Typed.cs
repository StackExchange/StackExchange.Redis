using System.Threading.Tasks.Sources;

namespace RESPite.Internal;

internal sealed class RespMessage<TResponse> : RespMessageBaseT<TResponse>
{
    private IRespParser<TResponse>? _parser;
}


