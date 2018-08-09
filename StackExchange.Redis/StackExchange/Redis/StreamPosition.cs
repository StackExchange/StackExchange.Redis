namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a pair consisting of the Stream Key and the <see cref="Position"/> from which to begin reading a stream.
    /// </summary>
    public struct StreamPosition
    {
        /// <summary>
        /// Initializes a <see cref="StreamPosition"/> value.
        /// </summary>
        /// <param name="key">The key for the stream.</param>
        /// <param name="position">The position from which to begin reading the stream.</param>
        public StreamPosition(RedisKey key, Position position)
        {
            Key = key;
            Position = position;
        }

        /// <summary>
        /// The stream key.
        /// </summary>
        public RedisKey Key { get; }

        /// <summary>
        /// The offset at which to begin reading the stream.
        /// </summary>
        public Position Position { get; }
    }
}
