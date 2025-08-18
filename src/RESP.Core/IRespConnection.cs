using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public interface IRespConnection : IDisposable, IAsyncDisposable
{
    RespPayload Send(RespPayload payload);
    ValueTask<RespPayload> SendAsync(RespPayload payload, CancellationToken cancellationToken = default);
}
