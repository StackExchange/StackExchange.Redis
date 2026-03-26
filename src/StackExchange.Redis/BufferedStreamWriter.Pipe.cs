using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal sealed class PipeStreamWriter : BufferedStreamWriter
{
    private readonly PipeWriter _writer;

    public PipeStreamWriter(Stream target, CancellationToken cancellationToken = default)
        : base(target, cancellationToken)
    {
        var pipe = new Pipe();
        WriteComplete = pipe.Reader.CopyToAsync(Target, cancellationToken);
        _writer = pipe.Writer;
    }

    public override Task WriteComplete { get; }

    private long _nonFlushed;
    public override void Advance(int count)
    {
        _nonFlushed += count;
        _writer.Advance(count);
    }

    public override void Flush()
    {
        var tmp = _nonFlushed;
        _nonFlushed = 0;
        OnWritten(tmp);
        var pending = _writer.FlushAsync();
        if (pending.IsCompleted)
        {
            pending.GetAwaiter().GetResult();
        }
        else
        {
            // this is bad, but: this type is a temporary kludge while I fix a bug;
            // this only happens during back-pressure events, which should be rare
            pending.AsTask().Wait(CancellationToken);
        }
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _writer.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _writer.GetSpan(sizeHint);

    public override void Complete(Exception? exception = null) => _writer.Complete(exception);
}
