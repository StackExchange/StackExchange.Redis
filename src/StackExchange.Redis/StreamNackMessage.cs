using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    public long StreamNegativeAcknowledge(RedisKey key, RedisValue groupName, RedisValue consumerName, StreamNackMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => ExecuteSync(GetStreamNegativeAcknowledgeMessage(key, groupName, consumerName, mode, messageId, flags), ResultProcessor.Int64);

    public Task<long> StreamNegativeAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, StreamNackMode mode, RedisValue messageId, CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(GetStreamNegativeAcknowledgeMessage(key, groupName, consumerName, mode, messageId, flags), ResultProcessor.Int64);

    public long StreamNegativeAcknowledge(RedisKey key, RedisValue groupName, RedisValue consumerName, StreamNackMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => ExecuteSync(GetStreamNegativeAcknowledgeMessage(key, groupName, consumerName, mode, messageIds, flags), ResultProcessor.Int64);

    public Task<long> StreamNegativeAcknowledgeAsync(RedisKey key, RedisValue groupName, RedisValue consumerName, StreamNackMode mode, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(GetStreamNegativeAcknowledgeMessage(key, groupName, consumerName, mode, messageIds, flags), ResultProcessor.Int64);

    private Message GetStreamNegativeAcknowledgeMessage(RedisKey key, RedisValue groupName, RedisValue consumerName, StreamNackMode mode, RedisValue messageId, CommandFlags flags)
        => new StreamNackMessageSingle(Database, flags, key, groupName, consumerName, mode, messageId);

    private Message GetStreamNegativeAcknowledgeMessage(RedisKey key, RedisValue groupName, RedisValue consumerName, StreamNackMode mode, RedisValue[] messageIds, CommandFlags flags)
        => messageIds is { Length: 1 }
            ? new StreamNackMessageSingle(Database, flags, key, groupName, consumerName, mode, messageIds[0])
            : new StreamNackMessageMulti(Database, flags, key, groupName, consumerName, mode, messageIds);

    internal abstract class StreamNackMessageBase : Message.CommandKeyBase
    {
        private readonly RedisValue groupName;
        private readonly RedisValue consumerName;
        private readonly StreamNackMode mode;

        protected StreamNackMessageBase(int db, CommandFlags flags, in RedisKey key, in RedisValue groupName, in RedisValue consumerName, StreamNackMode mode)
            : base(db, flags, RedisCommand.XNACK, key)
        {
            groupName.AssertNotNull();
            consumerName.AssertNotNull();

            this.groupName = groupName;
            this.consumerName = consumerName;
            this.mode = mode;
        }

        protected abstract int Count { get; }

        protected abstract void WriteIds(PhysicalConnection physical);

        protected override void WriteImpl(PhysicalConnection physical)
        {
            physical.WriteHeader(Command, ArgCount);
            physical.Write(Key);
            physical.WriteBulkString(groupName);
            physical.WriteBulkString(consumerName);
            WriteMode(physical);
            physical.WriteBulkString(StreamConstants.Ids);
            physical.WriteBulkString(Count);
            WriteIds(physical);
        }

        private void WriteMode(PhysicalConnection physical)
        {
            switch (mode)
            {
                case StreamNackMode.Silent:
                    physical.WriteBulkString("SILENT"u8);
                    break;
                case StreamNackMode.Fail:
                    physical.WriteBulkString("FAIL"u8);
                    break;
                case StreamNackMode.Fatal:
                    physical.WriteBulkString("FATAL"u8);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public override int ArgCount => 6 + Count;
    }

    internal sealed class StreamNackMessageSingle : StreamNackMessageBase
    {
        private readonly RedisValue messageId;

        public StreamNackMessageSingle(int db, CommandFlags flags, in RedisKey key, in RedisValue groupName, in RedisValue consumerName, StreamNackMode mode, in RedisValue messageId)
            : base(db, flags, key, groupName, consumerName, mode)
        {
            messageId.AssertNotNull();
            this.messageId = messageId;
        }

        protected override int Count => 1;

        protected override void WriteIds(PhysicalConnection physical) => physical.WriteBulkString(messageId);
    }

    internal sealed class StreamNackMessageMulti : StreamNackMessageBase
    {
        private readonly RedisValue[] messageIds;

        public StreamNackMessageMulti(int db, CommandFlags flags, in RedisKey key, in RedisValue groupName, in RedisValue consumerName, StreamNackMode mode, RedisValue[] messageIds)
            : base(db, flags, key, groupName, consumerName, mode)
        {
#if NET
            ArgumentNullException.ThrowIfNull(messageIds);
#else
            if (messageIds == null) throw new ArgumentNullException(nameof(messageIds));
#endif
            if (messageIds.Length == 0) throw new ArgumentOutOfRangeException(nameof(messageIds), "messageIds must contain at least one item.");

            for (int i = 0; i < messageIds.Length; i++)
            {
                messageIds[i].AssertNotNull();
            }

            this.messageIds = messageIds;
        }

        protected override int Count => messageIds.Length;

        protected override void WriteIds(PhysicalConnection physical)
        {
            for (int i = 0; i < messageIds.Length; i++)
            {
                physical.WriteBulkString(messageIds[i]);
            }
        }
    }
}
