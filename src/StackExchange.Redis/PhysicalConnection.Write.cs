using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class PhysicalConnection
{
    private BufferedStreamWriter? _output;
    private long TotalBytesSent => _output?.TotalBytesWritten ?? 0;
    public IBufferWriter<byte> Output
    {
        get
        {
            return _output ?? Throw();
            static IBufferWriter<byte> Throw() => throw new InvalidOperationException("Output pipe not initialized");
        }
    }

    private void InitOutput(Stream? stream)
    {
        if (stream is null) return;
        _ioStream = stream;
        _output = BufferedStreamWriter.Create(WriteMode, connectionType, stream, OutputCancel);
#if DEBUG
        if (BridgeCouldBeNull?.Multiplexer.RawConfig.OutputLog is { } log)
        {
            _output.DebugSetLog(log);
        }
#endif
    }

    internal bool HasOutputPipe => _output is not null;

    internal Task CompleteOutputAsync(Exception? exception = null)
    {
        if (_output is not { } output) return Task.CompletedTask;
        output.Complete(exception);
        return output.WriteComplete;
    }
}
