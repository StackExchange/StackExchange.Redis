
namespace StackExchange.Redis
{
    /// <summary>
    /// Describes a pair consisting of the Stream Key and the ID from which to read.
    /// </summary>
    /// <remarks><see cref="IDatabase.StreamRead(StreamIdPair[], int?, CommandFlags)"/></remarks>
    public struct StreamIdPair
    {
        /// <summary>
        /// Initializes a <see cref="StreamIdPair"/> value. 
        /// </summary>
        /// <param name="key">The key for the stream.</param>
        /// <param name="id">The ID from which to begin reading the stream.</param>
        public StreamIdPair(RedisKey key, RedisValue id)
        {
            Key = key;
            Id = id;
        }

        /// <summary>
        /// The key for the stream.
        /// </summary>
        public RedisKey Key { get; }

        /// <summary>
        /// The ID from which to begin reading the stream.
        /// </summary>
        public RedisValue Id { get; }

        /// <summary>
        /// See Object.ToString()
        /// </summary>
        public override string ToString() => $"{Key}: {Id}";
    }
}
