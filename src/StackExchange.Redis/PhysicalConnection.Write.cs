using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class PhysicalConnection
{
    private PipeWriter? _output;
    private long totalBytesSent;
    public IBufferWriter<byte> Output
    {
        get
        {
            return _output ?? Throw();
            static IBufferWriter<byte> Throw() => throw new InvalidOperationException("Output pipe not initialized");
        }
    }

    private void CreateOutputPipe()
    {
        if (_ioStream is not { } stream) return;
        var pipe = new Pipe();
        _output = pipe.Writer;
        _ = Task.Run(() => CopyOutputAsync(this, pipe.Reader, stream));
    }

    internal bool HasOutputPipe => _output is not null;

    private static async Task CopyOutputAsync(PhysicalConnection connection, PipeReader from, Stream to)
    {
        try
        {
            bool pendingFlush = false;
            while (true)
            {
                if (!from.TryRead(out var read))
                {
                    if (pendingFlush)
                    {
                        await to.FlushAsync(connection.OutputCancel).ConfigureAwait(false);
                        pendingFlush = false;
                    }
                    read = await from.ReadAsync(connection.OutputCancel).ConfigureAwait(false);
                }

                var buffer = read.Buffer;
                if (buffer.IsSingleSegment)
                {
                    var segment = buffer.First;
                    if (!segment.IsEmpty)
                    {
                        pendingFlush = true;
                        connection.totalBytesSent += segment.Length;
                        await to.WriteAsync(buffer.First, connection.OutputCancel).ConfigureAwait(false);
                    }
                }
                else
                {
                    foreach (var segment in buffer)
                    {
                        if (!segment.IsEmpty)
                        {
                            pendingFlush = true;
                            connection.totalBytesSent += segment.Length;
                            await to.WriteAsync(segment, connection.OutputCancel).ConfigureAwait(false);
                        }
                    }
                }
                from.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    break;
                }
            }

            await from.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                await from.CompleteAsync(ex).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }
    }
}
