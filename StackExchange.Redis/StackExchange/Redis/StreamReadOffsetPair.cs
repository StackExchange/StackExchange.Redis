
namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a pair consisting of the Stream Key and the Offset from which to begin reading a stream.
    /// </summary>
    public struct StreamReadOffsetPair
    {
        /// <summary>
        /// Initializes a <see cref="StreamGroupReadOffsetPair"/> value.
        /// </summary>
        /// <param name="key">The key for the stream.</param>
        /// <param name="readOffset">The offset at which to begin reading the stream. Defaults to <see cref="GroupReadOffset.New"/> when null.</param>
        public StreamReadOffsetPair(RedisKey key, ReadOffset readOffset)
        {
            Key = key;
            ReadOffset = readOffset;
        }

        /// <summary>
        /// The stream key.
        /// </summary>
        public RedisKey Key { get; }

        /// <summary>
        /// The offset at which to begin reading the stream.
        /// </summary>
        public ReadOffset ReadOffset { get; }

        /// <summary>
        /// Set Object.ToString().
        /// </summary>
        public override string ToString() => $"{Key}: {ReadOffset}";
    }
}
