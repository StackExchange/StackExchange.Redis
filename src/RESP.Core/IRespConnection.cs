using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public interface IRespConnection : IDisposable, IAsyncDisposable
{
    RespConfiguration Configuration { get; }
    bool CanWrite { get; }
    int Outstanding { get; }

    /// <summary>
    /// Gets the default context associates with this connection.
    /// </summary>
    ref readonly RespContext Context { get; }

    void Send(IRespMessage message);
    void Send(ReadOnlySpan<IRespMessage> messages);

    Task SendAsync(IRespMessage message);
    Task SendAsync(ReadOnlyMemory<IRespMessage> messages);
}
