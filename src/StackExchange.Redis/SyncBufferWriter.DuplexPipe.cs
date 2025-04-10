using System.IO;
using System.IO.Pipelines;

namespace StackExchange.Redis;

internal sealed class SyncDuplexPipe : IDuplexPipe
{
    public SyncDuplexPipe(Stream stream)
    {
        Input = new InputStreamSyncBufferReader(stream).Reader;
        var outbound = new OutputStreamSyncBufferWriter(stream);
        outbound.StartWriteLoop();
        Output = outbound;
    }

    public PipeReader Input { get; }

    public PipeWriter Output { get; }
}
