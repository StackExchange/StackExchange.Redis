using System.IO.Pipelines;
using RESPite.Streams;

namespace RESPite.Proxy;

internal sealed class ProxyClient(ProxyServer server, int id, IDuplexPipe transport)
    : RespStream(transport.Input.AsStream())
{
    public int Id => id;
    public int Database => _db;
    private int _db;
    private TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void OnSelect(int db) => _db = db;
    public Task ExecuteAsync()
    {
        StartReading(sync: false, cancellationToken: server.Lifetime);
        return _completionSource.Task;
    }

    public void SendResponse(ReadOnlySpan<byte> frame)
    {
    }
}
