using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public interface IRespConnection : IDisposable, IAsyncDisposable
{
    RespConfiguration Configuration { get; }
    bool CanWrite { get; }
    int Outstanding { get; }

    void Send(IRespMessage message);
    Task SendAsync(IRespMessage message, CancellationToken cancellationToken = default);
}
