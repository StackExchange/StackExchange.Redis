using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    public long StreamNegativeAcknowledge(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => ExecuteSync(GetStreamNegativeAcknowledgeMessage(key, groupName, mode, messageId, flags), ResultProcessor.Int64);

    public Task<long> StreamNegativeAcknowledgeAsync(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(GetStreamNegativeAcknowledgeMessage(key, groupName, mode, messageId, flags), ResultProcessor.Int64);

    public long StreamNegativeAcknowledge(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => ExecuteSync(GetStreamNegativeAcknowledgeMessage(key, groupName, mode, messageIds, flags), ResultProcessor.Int64);

    public Task<long> StreamNegativeAcknowledgeAsync(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(GetStreamNegativeAcknowledgeMessage(key, groupName, mode, messageIds, flags), ResultProcessor.Int64);

    private Message GetStreamNegativeAcknowledgeMessage(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue messageId, CommandFlags flags)
        => new StreamNackMessageSingle(Database, flags, key, groupName, mode, messageId);

    private Message GetStreamNegativeAcknowledgeMessage(RedisKey key, RedisValue groupName, StreamNackMode mode, RedisValue[] messageIds, CommandFlags flags)
        => messageIds is { Length: 1 }
            ? new StreamNackMessageSingle(Database, flags, key, groupName, mode, messageIds[0])
            : new StreamNackMessageMulti(Database, flags, key, groupName, mode, messageIds);

    internal abstract class StreamNackMessageBase : Message.CommandKeyBase
    {
        private readonly RedisValue groupName;
        private readonly StreamNackMode mode;

        protected StreamNackMessageBase(int db, CommandFlags flags, in RedisKey key, in RedisValue groupName, StreamNackMode mode)
            : base(db, flags, RedisCommand.XNACK, key)
        {
            groupName.AssertNotNull();

            this.groupName = groupName;
            this.mode = mode;
        }

        protected abstract int Count { get; }

        protected abstract void WriteIds(in MessageWriter writer);

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, ArgCount);
            writer.Write(Key);
            writer.WriteBulkString(groupName);
            WriteMode(writer);
            writer.WriteBulkString(StreamConstants.Ids);
            writer.WriteBulkString(Count);
            WriteIds(writer);
        }

        private void WriteMode(in MessageWriter writer)
        {
            switch (mode)
            {
                case StreamNackMode.Silent:
                    writer.WriteRaw("$6\r\nSILENT\r\n"u8);
                    break;
                case StreamNackMode.Fail:
                    writer.WriteRaw("$4\r\nFAIL\r\n"u8);
                    break;
                case StreamNackMode.Fatal:
                    writer.WriteRaw("$5\r\nFATAL\r\n"u8);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public override int ArgCount => 5 + Count;
    }

    internal sealed class StreamNackMessageSingle : StreamNackMessageBase
    {
        private readonly RedisValue messageId;

        public StreamNackMessageSingle(int db, CommandFlags flags, in RedisKey key, in RedisValue groupName, StreamNackMode mode, in RedisValue messageId)
            : base(db, flags, key, groupName, mode)
        {
            messageId.AssertNotNull();
            this.messageId = messageId;
        }

        protected override int Count => 1;

        protected override void WriteIds(in MessageWriter writer) => writer.WriteBulkString(messageId);
    }

    internal sealed class StreamNackMessageMulti : StreamNackMessageBase
    {
        private readonly RedisValue[] messageIds;

        public StreamNackMessageMulti(int db, CommandFlags flags, in RedisKey key, in RedisValue groupName, StreamNackMode mode, RedisValue[] messageIds)
            : base(db, flags, key, groupName, mode)
        {
#if NET
            ArgumentNullException.ThrowIfNull(messageIds);
#else
            if (messageIds == null) throw new ArgumentNullException(nameof(messageIds));
#endif
            if (messageIds.Length == 0) throw new ArgumentOutOfRangeException(nameof(messageIds), "messageIds must contain at least one item.");
            this.messageIds = messageIds.AssertAllNonNull();
        }

        protected override int Count => messageIds.Length;

        protected override void WriteIds(in MessageWriter writer)
        {
            for (int i = 0; i < messageIds.Length; i++)
            {
                writer.WriteBulkString(messageIds[i]);
            }
        }
    }
}
