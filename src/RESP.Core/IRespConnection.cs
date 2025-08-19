using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public interface IRespConnection : IDisposable, IAsyncDisposable
{
    bool CanWrite { get; }
    int Outstanding { get; }

    RespPayload Send(RespPayload payload);
    ValueTask<RespPayload> SendAsync(RespPayload payload, CancellationToken cancellationToken = default);
}
