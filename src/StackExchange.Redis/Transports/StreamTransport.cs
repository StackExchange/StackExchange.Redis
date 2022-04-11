using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
#nullable enable

namespace StackExchange.Redis.Transports
{
    internal sealed class StreamTransport : Transport
    {
        private readonly Stream _duplexStream;

        public StreamTransport(Stream duplexStream, int inputBufferSize, int outputBufferSize, ILogger? logger, RefCountedMemoryPool<byte>? pool, ServerEndPoint server, bool pubsub)
            : base(inputBufferSize, outputBufferSize, logger, pool, server, pubsub)
        {
            _duplexStream = duplexStream!;
            if (duplexStream is null) ThrowNull(nameof(duplexStream));
            static void ThrowNull(string parameterName) => throw new ArgumentNullException(parameterName);
        }

        protected override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => _duplexStream.ReadAsync(buffer, cancellationToken);
        protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) => _duplexStream.WriteAsync(buffer, cancellationToken);
        protected override ValueTask WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => new ValueTask(_duplexStream.WriteAsync(buffer, offset, count, cancellationToken));
        protected override ValueTask FlushAsync(CancellationToken cancellationToken) => new ValueTask(_duplexStream.FlushAsync(cancellationToken));
    }
}
