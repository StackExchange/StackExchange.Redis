
namespace StackExchange.Redis
{
    ///<summary>
    ///Options for reading messages from a stream with a consumer group.
    ///</summary>
    ///<remarks>https://redis.io/commands/xreadgroup</remarks>
    public struct GroupReadOffset
    {
        private GroupReadOffset(RedisValue value)
        {
            CommandValue = value;
        }

        internal RedisValue CommandValue { get; }

        ///<summary>
        ///Read new messages that haven't been delivered to a consumer.
        ///</summary>
        public static GroupReadOffset New = new GroupReadOffset(">");

        ///<summary>
        ///Read all pending messages in a consumer group or, if no messages have been read into the group,
        ///begin reading at the beginning of the stream.
        ///</summary>
        public static GroupReadOffset All = new GroupReadOffset("-");

        ///<summary>
        ///Read pending messages in a consumer group after the given ID or, if no messages
        ///have been read into the group, begin reading after the given message ID.
        ///</summary>
        public static GroupReadOffset AfterId(RedisValue messageId) => new GroupReadOffset(messageId);

        /// <summary>
        /// Set Object.ToString().
        /// </summary>
        public override string ToString() => CommandValue;
    }
}
