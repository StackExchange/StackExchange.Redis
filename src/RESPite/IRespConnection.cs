using System;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite;

public interface IRespConnection : IDisposable, IAsyncDisposable
{
    RespConfiguration Configuration { get; }
    bool CanWrite { get; }
    int Outstanding { get; }

    /// <summary>
    /// Gets the default context associates with this connection.
    /// </summary>
    ref readonly RespContext Context { get; }

    void Send(RespOperation message);

    Task SendAsync(RespOperation message);
}
