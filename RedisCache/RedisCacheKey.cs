using System.Collections.Generic;
using System.Linq;

namespace Saxo.RedisCache
{
    public class RedisCacheKey
    {
        public string PrimaryKey { get; }
        public List<string> SecondaryKeys { get; }

        /// <summary>
        /// Initializes a new instance of RedisCacheKey that is empty. Only accessible internally.
        /// </summary>
        private RedisCacheKey()
        {
            SecondaryKeys = new List<string>();
        }

        /// <summary>
        /// Initializes a new instance of RedisCacheKey holding only a primary key.
        /// </summary>
        /// <param name="primaryKey">A primary key assumed to be unique.</param>
        public RedisCacheKey(string primaryKey): this()
        {
            PrimaryKey = primaryKey;
        }

        /// <summary>
        /// Initializes a new instance of RedisCacheKey holding a primary key and a single secondary key.
        /// </summary>
        /// <param name="primaryKey">A primary key assumed to be unique.</param>
        /// <param name="secondaryKey">A secondary key assumed to be unique.</param>
        public RedisCacheKey(string primaryKey, string secondaryKey) : this(primaryKey)
        {
            SecondaryKeys.Add(secondaryKey);
        }

        /// <summary>
        /// Initializes a new instance of RedisCacheKey holding only a list of secondary keys.
        /// </summary>
        /// <param name="secondaryKeys">A list of secondary keys assumed to be unique.</param>
        public RedisCacheKey(IEnumerable<string> secondaryKeys) : this()
        {
            SecondaryKeys.AddRange(secondaryKeys);
        }

        /// <summary>
        /// Initializes a new instance of RedisCacheKey holding a primary key and a list of secondary keys.
        /// </summary>
        /// <param name="primaryKey">A primary key assumed to be unique.</param>
        /// <param name="secondaryKeys">A list of secondary keys assumed to be unique.</param>
        public RedisCacheKey(string primaryKey, IEnumerable<string> secondaryKeys) : this(primaryKey)
        {
            SecondaryKeys.AddRange(secondaryKeys);
        }

        /// <summary>
        /// Checks whether a primary key has been defined for this cache key object.
        /// </summary>
        public bool HasPrimaryKey => !string.IsNullOrEmpty(PrimaryKey);

        /// <summary>
        /// Checks whether a secondary key has been defined for this cache key object.
        /// </summary>
        public bool HasSecondaryKey => SecondaryKeys.Any();

    }
}
