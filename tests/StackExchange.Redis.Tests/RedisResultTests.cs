using System;
using System.Collections.Generic;
using Xunit;

namespace StackExchange.Redis.Tests
{
    /// <summary>
    /// Tests for <see cref="RedisResult"/>
    /// </summary>
    public sealed class RedisResultTests
    {
        /// <summary>
        /// Tests the basic functionality of <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/>
        /// </summary>
        [Fact]
        public void ToDictionaryWorks()
        {
            var redisArrayResult = RedisResult.Create(
                new RedisValue[] { "one", 1, "two", 2, "three", 3, "four", 4 });

            var dict = redisArrayResult.ToDictionary();

            Assert.Equal(4, dict.Count);
            Assert.Equal(1, (RedisValue)dict["one"]);
            Assert.Equal(2, (RedisValue)dict["two"]);
            Assert.Equal(3, (RedisValue)dict["three"]);
            Assert.Equal(4, (RedisValue)dict["four"]);
        }

        /// <summary>
        /// Tests the basic functionality of <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/>
        /// when the results contain a nested results array, which is common for lua script results
        /// </summary>
        [Fact]
        public void ToDictionaryWorksWhenNested()
        {
            var redisArrayResult = RedisResult.Create(
                new RedisResult[]
                {
                    RedisResult.Create((RedisValue)"one"),
                    RedisResult.Create(new RedisValue[]{"two", 2, "three", 3}),

                    RedisResult.Create((RedisValue)"four"),
                    RedisResult.Create(new RedisValue[] { "five", 5, "six", 6 }),
                });

            var dict = redisArrayResult.ToDictionary();
            var nestedDict = dict["one"].ToDictionary();

            Assert.Equal(2, dict.Count);
            Assert.Equal(2, nestedDict.Count);
            Assert.Equal(2, (RedisValue)nestedDict["two"]);
            Assert.Equal(3, (RedisValue)nestedDict["three"]);
        }

        /// <summary>
        /// Tests that <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/> fails when a duplicate key is encountered.
        /// This also tests that the default comparator is case-insensitive.
        /// </summary>
        [Fact]
        public void ToDictionaryFailsWithDuplicateKeys()
        {
            var redisArrayResult = RedisResult.Create(
                new RedisValue[] { "banana", 1, "BANANA", 2, "orange", 3, "apple", 4 });

            Assert.Throws<ArgumentException>(() => redisArrayResult.ToDictionary(/* Use default comparer, causes collision of banana */));
        }

        /// <summary>
        /// Tests that <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/> correctly uses the provided comparator
        /// </summary>
        [Fact]
        public void ToDictionaryWorksWithCustomComparator()
        {
            var redisArrayResult = RedisResult.Create(
                new RedisValue[] { "banana", 1, "BANANA", 2, "orange", 3, "apple", 4 });

            var dict = redisArrayResult.ToDictionary(StringComparer.Ordinal);

            Assert.Equal(4, dict.Count);
            Assert.Equal(1, (RedisValue)dict["banana"]);
            Assert.Equal(2, (RedisValue)dict["BANANA"]);
        }

        /// <summary>
        /// Tests that <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/> fails when the redis results array contains an odd number
        /// of elements.  In other words, it's not actually a Key,Value,Key,Value... etc. array
        /// </summary>
        [Fact]
        public void ToDictionaryFailsOnMishapenResults()
        {
            var redisArrayResult = RedisResult.Create(
                new RedisValue[] { "one", 1, "two", 2, "three", 3, "four" /* missing 4 */ });

            Assert.Throws<IndexOutOfRangeException>(()=>redisArrayResult.ToDictionary(StringComparer.Ordinal));
        }
    }
}
