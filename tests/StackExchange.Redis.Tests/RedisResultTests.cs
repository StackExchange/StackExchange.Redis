using System;
using System.Collections.Generic;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for <see cref="RedisResult"/>.
/// </summary>
public sealed class RedisResultTests
{
    /// <summary>
    /// Tests the basic functionality of <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/>.
    /// </summary>
    [Fact]
    public void ToDictionaryWorks()
    {
        var redisArrayResult = RedisResult.Create(
            ["one", 1, "two", 2, "three", 3, "four", 4]);

        var dict = redisArrayResult.ToDictionary();

        Assert.Equal(4, dict.Count);
        Assert.Equal(1, (RedisValue)dict["one"]);
        Assert.Equal(2, (RedisValue)dict["two"]);
        Assert.Equal(3, (RedisValue)dict["three"]);
        Assert.Equal(4, (RedisValue)dict["four"]);
    }

    /// <summary>
    /// Tests the basic functionality of <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/>
    /// when the results contain a nested results array, which is common for lua script results.
    /// </summary>
    [Fact]
    public void ToDictionaryWorksWhenNested()
    {
        var redisArrayResult = RedisResult.Create(
            [
                RedisResult.Create((RedisValue)"one"),
                RedisResult.Create(["two", 2, "three", 3]),

                RedisResult.Create((RedisValue)"four"),
                RedisResult.Create(["five", 5, "six", 6]),
            ]);

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
            ["banana", 1, "BANANA", 2, "orange", 3, "apple", 4]);

        Assert.Throws<ArgumentException>(() => redisArrayResult.ToDictionary(/* Use default comparer, causes collision of banana */));
    }

    /// <summary>
    /// Tests that <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/> correctly uses the provided comparator.
    /// </summary>
    [Fact]
    public void ToDictionaryWorksWithCustomComparator()
    {
        var redisArrayResult = RedisResult.Create(
            ["banana", 1, "BANANA", 2, "orange", 3, "apple", 4]);

        var dict = redisArrayResult.ToDictionary(StringComparer.Ordinal);

        Assert.Equal(4, dict.Count);
        Assert.Equal(1, (RedisValue)dict["banana"]);
        Assert.Equal(2, (RedisValue)dict["BANANA"]);
    }

    /// <summary>
    /// Tests that <see cref="RedisResult.ToDictionary(IEqualityComparer{string})"/> fails when the redis results array contains an odd number
    /// of elements.  In other words, it's not actually a Key,Value,Key,Value... etc. array.
    /// </summary>
    [Fact]
    public void ToDictionaryFailsOnMishapenResults()
    {
        var redisArrayResult = RedisResult.Create(
            ["one", 1, "two", 2, "three", 3, "four" /* missing 4 */]);

        Assert.Throws<IndexOutOfRangeException>(() => redisArrayResult.ToDictionary(StringComparer.Ordinal));
    }

    [Fact]
    public void SingleResultConvertibleViaTo()
    {
        var value = RedisResult.Create(123);
        Assert.StrictEqual((int)123, Convert.ToInt32(value));
        Assert.StrictEqual((uint)123U, Convert.ToUInt32(value));
        Assert.StrictEqual(123L, Convert.ToInt64(value));
        Assert.StrictEqual(123UL, Convert.ToUInt64(value));
        Assert.StrictEqual((byte)123, Convert.ToByte(value));
        Assert.StrictEqual((sbyte)123, Convert.ToSByte(value));
        Assert.StrictEqual((short)123, Convert.ToInt16(value));
        Assert.StrictEqual((ushort)123, Convert.ToUInt16(value));
        Assert.Equal("123", Convert.ToString(value));
        Assert.StrictEqual(123M, Convert.ToDecimal(value));
        Assert.StrictEqual((char)123, Convert.ToChar(value));
        Assert.StrictEqual(123f, Convert.ToSingle(value));
        Assert.StrictEqual(123d, Convert.ToDouble(value));
    }

    [Fact]
    public void SingleResultConvertibleDirectViaChangeType_Type()
    {
        var value = RedisResult.Create(123);
        Assert.StrictEqual((int)123, Convert.ChangeType(value, typeof(int)));
        Assert.StrictEqual((uint)123U, Convert.ChangeType(value, typeof(uint)));
        Assert.StrictEqual(123L, Convert.ChangeType(value, typeof(long)));
        Assert.StrictEqual(123UL, Convert.ChangeType(value, typeof(ulong)));
        Assert.StrictEqual((byte)123, Convert.ChangeType(value, typeof(byte)));
        Assert.StrictEqual((sbyte)123, Convert.ChangeType(value, typeof(sbyte)));
        Assert.StrictEqual((short)123, Convert.ChangeType(value, typeof(short)));
        Assert.StrictEqual((ushort)123, Convert.ChangeType(value, typeof(ushort)));
        Assert.Equal("123", Convert.ChangeType(value, typeof(string)));
        Assert.StrictEqual(123M, Convert.ChangeType(value, typeof(decimal)));
        Assert.StrictEqual((char)123, Convert.ChangeType(value, typeof(char)));
        Assert.StrictEqual(123f, Convert.ChangeType(value, typeof(float)));
        Assert.StrictEqual(123d, Convert.ChangeType(value, typeof(double)));
    }

    [Fact]
    public void SingleResultConvertibleDirectViaChangeType_TypeCode()
    {
        var value = RedisResult.Create(123);
        Assert.StrictEqual((int)123, Convert.ChangeType(value, TypeCode.Int32));
        Assert.StrictEqual((uint)123U, Convert.ChangeType(value, TypeCode.UInt32));
        Assert.StrictEqual(123L, Convert.ChangeType(value, TypeCode.Int64));
        Assert.StrictEqual(123UL, Convert.ChangeType(value, TypeCode.UInt64));
        Assert.StrictEqual((byte)123, Convert.ChangeType(value, TypeCode.Byte));
        Assert.StrictEqual((sbyte)123, Convert.ChangeType(value, TypeCode.SByte));
        Assert.StrictEqual((short)123, Convert.ChangeType(value, TypeCode.Int16));
        Assert.StrictEqual((ushort)123, Convert.ChangeType(value, TypeCode.UInt16));
        Assert.Equal("123", Convert.ChangeType(value, TypeCode.String));
        Assert.StrictEqual(123M, Convert.ChangeType(value, TypeCode.Decimal));
        Assert.StrictEqual((char)123, Convert.ChangeType(value, TypeCode.Char));
        Assert.StrictEqual(123f, Convert.ChangeType(value, TypeCode.Single));
        Assert.StrictEqual(123d, Convert.ChangeType(value, TypeCode.Double));
    }
}
