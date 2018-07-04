
namespace StackExchange.Redis
{
    /// <summary>
    /// Options for where to begin reading a stream for a Consumer Group.
    /// </summary>
    /// <remarks>https://redis.io/topics/streams-intro</remarks>
    public struct GroupCreateOptions
    {
        private GroupCreateOptions(RedisValue value)
        {
            CommandValue = value;
        }

        internal RedisValue CommandValue { get; }

        /// <summary>
        /// Begin reading only new messages arriving in the stream after the group is created.
        /// </summary>
        public static GroupCreateOptions ReadNew = new GroupCreateOptions("$");

        /// <summary>
        /// Begin reading the stream from its beginning.
        /// </summary>
        public static GroupCreateOptions ReadBeginning = new GroupCreateOptions("-");

        /// <summary>
        /// Begin reading the stream after the given message ID.
        /// </summary>
        /// <param name="afterId">Read the stream after the given ID.</param>
        public static GroupCreateOptions ReadAfterId(RedisValue afterId)
            => new GroupCreateOptions(afterId);
    }
}
