using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

internal sealed class RespConnection(Stream tail) : IRespConnection
{
    public RespPayload Send(RespPayload payload)
    {
        _ = tail;
        throw new NotImplementedException();
    }

    public ValueTask<RespPayload> SendAsync(RespPayload payload, CancellationToken cancellationToken = default)
    {
        _ = tail;
        throw new NotImplementedException();
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
