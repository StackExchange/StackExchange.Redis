
namespace StackExchange.Redis
{
    /// <summary>
    /// Options for where to read in a stream.
    /// </summary>
    /// <remarks>https://redis.io/commands/xread</remarks>
    public struct ReadOffset
    {
        private ReadOffset(RedisValue value)
        {
            CommandValue = value;
        }

        internal RedisValue CommandValue { get; }

        /// <summary>
        /// Read from the lowest message ID in the stream.
        /// </summary>
        public static ReadOffset FromBeginning = new ReadOffset("-");

        /// <summary>
        /// Read after the given message ID in the stream.
        /// </summary>
        /// <param name="id"></param>
        public static ReadOffset AfterId(RedisValue id) => new ReadOffset(id);

        /// <summary>
        /// Set Object.ToString().
        /// </summary>
        public override string ToString() => CommandValue;
    }
}
