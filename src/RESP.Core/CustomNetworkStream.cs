// using System;
// using System.Buffers;
// using System.IO;
// using System.Net.Sockets;
// using System.Threading;
// using System.Threading.Tasks;
//
// namespace Resp;
//
// internal sealed class CustomNetworkStream(Socket socket) : Stream
// {
//     public override void Close()
//     {
//         socket.Close();
//     }
//
//     protected override void Dispose(bool disposing)
//     {
//         if (disposing)
//         {
//             socket.Dispose();
//         }
//
//         base.Dispose(disposing);
//     }
//
//     public override void Flush() { }
//
//     public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
//
//     public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
//
//     public override void SetLength(long value) => throw new NotSupportedException();
//
//     public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
//
//     public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
//
//     public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
//     {
//         if (!cancellationToken.CanBeCanceled) return socket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, count));
//
//         return socket.ReceiveAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
//     }
//
//     public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
//     {
//         if (!cancellationToken.CanBeCanceled) return socket.SendAsync(new ArraySegment<byte>(buffer, offset, count));
// #if NET6_0_OR_GREATER
//         return socket.SendAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);
// #else
//         var oversized = ArrayPool<byte>.Shared.Rent(count);
//         new ReadOnlySpan<byte>(buffer, offset, count).CopyTo(oversized);
//         using var reg = cancellationToken.Register(CancellationCallback, this);
//         var pending = socket.SendAsync(new ArraySegment<byte>(oversized, 0, count));
//         return socket.SendAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();
// #endif
//     }
//
//     private void Kill() => socket.Dispose(); // cancellation leaves socket in broken state
//     private static readonly Action<object?> CancellationCallback = static state => ((CustomNetworkStream)state!).Kill();
//
// #if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
//     public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
//         => socket.ReceiveAsync(buffer, cancellationToken);
//
//     public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
//     {
//         var pending = socket.SendAsync(buffer, cancellationToken);
//         if (!pending.IsCompleted) return new(pending.AsTask());
//         pending.GetAwaiter().GetResult();
//         return default;
//     }
//
//     public override int Read(Span<byte> buffer) => socket.Receive(buffer);
//
//     public override void Write(ReadOnlySpan<byte> buffer) => socket.Send(buffer);
// #endif
//
//     public override bool CanRead => true;
//     public override bool CanSeek => false;
//     public override bool CanWrite => true;
//     public override long Length => throw new NotSupportedException();
//
//     public override long Position
//     {
//         get => throw new NotSupportedException();
//         set => throw new NotSupportedException();
//     }
//     public override bool CanTimeout => socket.ReceiveTimeout != 0 || socket.SendTimeout != 0;
//     public override int ReadTimeout
//     {
//         get => socket.ReceiveTimeout;
//         set => socket.ReceiveTimeout = value;
//     }
//     public override int WriteTimeout
//     {
//         get => socket.SendTimeout;
//         set => socket.SendTimeout = value;
//     }
// }
