using System;
using System.Buffers;
using System.Diagnostics;
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

    private Task _writeComplete = Task.CompletedTask;

    private void InitOutput(Stream? stream)
    {
        if (stream is null) return;
        _ioStream = stream;
        var pipe = new Pipe();
        _output = pipe.Writer;
        _writeComplete = Task.Run(() => CopyOutputAsync(this, pipe.Reader), OutputCancel);
    }

    internal bool HasOutputPipe => _output is not null;

    internal Task CompleteOutputAsync(Exception? exception = null)
    {
        _output?.Complete(exception);
        return _writeComplete;
    }

    private static async Task CopyOutputAsync(PhysicalConnection connection, PipeReader from)
    {
        try
        {
            bool pendingFlush = false;
            while (connection._ioStream is { } stream)
            {
                if (!from.TryRead(out var read))
                {
                    if (pendingFlush)
                    {
                        await stream.FlushAsync(connection.OutputCancel).ConfigureAwait(false);
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
                        await stream.WriteAsync(buffer.First, connection.OutputCancel).ConfigureAwait(false);
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
                            await stream.WriteAsync(segment, connection.OutputCancel).ConfigureAwait(false);
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
