using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace StackExchange.Redis
{
#if LOGOUTPUT
    sealed class LoggingPipe : IDuplexPipe
    {
        private IDuplexPipe _inner;

        public LoggingPipe(IDuplexPipe inner, string inPath, string outPath, SocketManager mgr)
        {
            _inner = inner;
            if (string.IsNullOrWhiteSpace(inPath))
            {
                Input = inner.Input;
            }
            else
            {
                var pipe = new Pipe(mgr.ReceivePipeOptions);
                Input = pipe.Reader;
                CloneAsync(inPath, inner.Input, pipe.Writer).RedisFireAndForget();
            }

            if (string.IsNullOrWhiteSpace(outPath))
            {
                Output = inner.Output;
            }
            else
            {
                var pipe = new Pipe(mgr.SendPipeOptions);
                Output = pipe.Writer;
                CloneAsync(outPath, pipe.Reader, inner.Output).RedisFireAndForget();
            }

        }

        private async Task CloneAsync(string path, PipeReader from, PipeWriter to)
        {
            try {
                to.OnReaderCompleted((ex, o) => {
                    // if (ex != null) Console.Error.WriteLine(ex);
                    ((PipeReader)o).Complete(ex);                
                }, from);
                from.OnWriterCompleted((ex, o) =>
                {
                    // if (ex != null) Console.Error.WriteLine(ex);
                    ((PipeWriter)o).Complete(ex);
                }, to);
                try { File.Delete(path); } catch{}

                while(true)
                {
                    var result = await from.ReadAsync();
                    var buffer = result.Buffer;
                    if (result.IsCompleted && buffer.IsEmpty) break;

                    using (var file = new FileStream(path, FileMode.Append, FileAccess.Write))
                    {
                        foreach (var segment in buffer)
                        {
                            // append it to the file
                            bool leased = false;
                            if (!MemoryMarshal.TryGetArray(segment, out var arr))
                            {
                                var tmp = ArrayPool<byte>.Shared.Rent(segment.Length);
                                segment.CopyTo(tmp);
                                arr = new ArraySegment<byte>(tmp, 0, segment.Length);
                                leased = true;
                            }
                            await file.WriteAsync(arr.Array, arr.Offset, arr.Count).ForAwait();
                            await file.FlushAsync().ForAwait();
                            if (leased) ArrayPool<byte>.Shared.Return(arr.Array);

                            // and flush it upstream
                            await to.WriteAsync(segment).ForAwait();
                        }
                    }
                    from.AdvanceTo(buffer.End);
                }
            }
            catch { }
        }
        public PipeReader Input { get; }

        public PipeWriter Output { get; }
    }
#endif
}
