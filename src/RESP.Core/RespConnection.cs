using System;
using System.IO;
using System.Threading.Tasks;

namespace Resp;

internal class RespConnection(Stream tail)
{
    public RespPayload Send(RespPayload payload)
    {
        _ = tail;
        throw new NotImplementedException();
    }

    public ValueTask<RespPayload> SendAsync(RespPayload payload)
    {
        _ = tail;
        throw new NotImplementedException();
    }
}
