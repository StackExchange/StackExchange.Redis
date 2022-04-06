using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StackExchange.Redis.Connections
{
    internal interface IRedisConnection : IAsyncEnumerable<RawResult>
    {
        Task WriteAllAsync(ChannelReader<WrittenMessage> messages, CancellationToken cancellationToken);
    }

    internal readonly struct WrittenMessage
    {
        private readonly Message _message;
        private readonly ReadOnlySequence<byte> _payload;

        public WrittenMessage(ReadOnlySequence<byte> payload, Message message)
        {
            _message = message;
            _payload = payload;
        }
    }
}
