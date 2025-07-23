using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class BoxUnboxTests
{
    [Theory]
    [MemberData(nameof(RoundTripValues))]
    public void RoundTripRedisValue(RedisValue value)
    {
        var boxed = value.Box();
        var unboxed = RedisValue.Unbox(boxed);
        AssertEqualGiveOrTakeNaN(value, unboxed);
    }

    [Theory]
    [MemberData(nameof(UnboxValues))]
    public void UnboxCommonValues(object value, RedisValue expected)
    {
        var unboxed = RedisValue.Unbox(value);
        AssertEqualGiveOrTakeNaN(expected, unboxed);
    }

    [Theory]
    [MemberData(nameof(InternedValues))]
    public void ReturnInternedBoxesForCommonValues(RedisValue value, bool expectSameReference)
    {
        object? x = value.Box(), y = value.Box();
        Assert.Equal(expectSameReference, ReferenceEquals(x, y));
        // check we got the right values!
        AssertEqualGiveOrTakeNaN(value, RedisValue.Unbox(x));
        AssertEqualGiveOrTakeNaN(value, RedisValue.Unbox(y));
    }

    private static void AssertEqualGiveOrTakeNaN(RedisValue expected, RedisValue actual)
    {
        if (expected.Type == RedisValue.StorageType.Double && actual.Type == expected.Type)
        {
            // because NaN != NaN, we need to special-case this scenario
            bool enan = double.IsNaN((double)expected), anan = double.IsNaN((double)actual);
            if (enan | anan)
            {
                Assert.Equal(enan, anan);
                return; // and that's all
            }
        }
        Assert.Equal(expected, actual);
    }

    private static readonly byte[] s_abc = Encoding.UTF8.GetBytes("abc");
    public static IEnumerable<object[]> RoundTripValues
        => new[]
        {
            new object[] { RedisValue.Null },
            [RedisValue.EmptyString],
            [(RedisValue)0L],
            [(RedisValue)1L],
            [(RedisValue)18L],
            [(RedisValue)19L],
            [(RedisValue)20L],
            [(RedisValue)21L],
            [(RedisValue)22L],
            [(RedisValue)(-1L)],
            [(RedisValue)0],
            [(RedisValue)1],
            [(RedisValue)18],
            [(RedisValue)19],
            [(RedisValue)20],
            [(RedisValue)21],
            [(RedisValue)22],
            [(RedisValue)(-1)],
            [(RedisValue)0F],
            [(RedisValue)1F],
            [(RedisValue)(-1F)],
            [(RedisValue)0D],
            [(RedisValue)1D],
            [(RedisValue)(-1D)],
            [(RedisValue)float.PositiveInfinity],
            [(RedisValue)float.NegativeInfinity],
            [(RedisValue)float.NaN],
            [(RedisValue)double.PositiveInfinity],
            [(RedisValue)double.NegativeInfinity],
            [(RedisValue)double.NaN],
            [(RedisValue)true],
            [(RedisValue)false],
            [(RedisValue)(string?)null],
            [(RedisValue)"abc"],
            [(RedisValue)s_abc],
            [(RedisValue)new Memory<byte>(s_abc)],
            [(RedisValue)new ReadOnlyMemory<byte>(s_abc)],
        };

    public static IEnumerable<object?[]> UnboxValues
        => new[]
        {
            new object?[] { null, RedisValue.Null },
            ["", RedisValue.EmptyString],
            [0, (RedisValue)0],
            [1, (RedisValue)1],
            [18, (RedisValue)18],
            [19, (RedisValue)19],
            [20, (RedisValue)20],
            [21, (RedisValue)21],
            [22, (RedisValue)22],
            [-1, (RedisValue)(-1)],
            [18L, (RedisValue)18],
            [19L, (RedisValue)19],
            [20L, (RedisValue)20],
            [21L, (RedisValue)21],
            [22L, (RedisValue)22],
            [-1L, (RedisValue)(-1)],
            [0F, (RedisValue)0],
            [1F, (RedisValue)1],
            [-1F, (RedisValue)(-1)],
            [0D, (RedisValue)0],
            [1D, (RedisValue)1],
            [-1D, (RedisValue)(-1)],
            [float.PositiveInfinity, (RedisValue)double.PositiveInfinity],
            [float.NegativeInfinity, (RedisValue)double.NegativeInfinity],
            [float.NaN, (RedisValue)double.NaN],
            [double.PositiveInfinity, (RedisValue)double.PositiveInfinity],
            [double.NegativeInfinity, (RedisValue)double.NegativeInfinity],
            [double.NaN, (RedisValue)double.NaN],
            [true, (RedisValue)true],
            [false, (RedisValue)false],
            ["abc", (RedisValue)"abc"],
            [s_abc, (RedisValue)s_abc],
            [new Memory<byte>(s_abc), (RedisValue)s_abc],
            [new ReadOnlyMemory<byte>(s_abc), (RedisValue)s_abc],
            [(RedisValue)1234, (RedisValue)1234],
        };

    public static IEnumerable<object[]> InternedValues()
    {
        for (int i = -20; i <= 40; i++)
        {
            bool expectInterned = i >= -1 & i <= 20;
            yield return new object[] { (RedisValue)i, expectInterned };
            yield return new object[] { (RedisValue)(long)i, expectInterned };
            yield return new object[] { (RedisValue)(float)i, expectInterned };
            yield return new object[] { (RedisValue)(double)i, expectInterned };
        }

        yield return new object[] { (RedisValue)float.NegativeInfinity, true };
        yield return new object[] { (RedisValue)(-0.5F), false };
        yield return new object[] { (RedisValue)0.5F, false };
        yield return new object[] { (RedisValue)float.PositiveInfinity, true };
        yield return new object[] { (RedisValue)float.NaN, true };

        yield return new object[] { (RedisValue)double.NegativeInfinity, true };
        yield return new object[] { (RedisValue)(-0.5D), false };
        yield return new object[] { (RedisValue)0.5D, false };
        yield return new object[] { (RedisValue)double.PositiveInfinity, true };
        yield return new object[] { (RedisValue)double.NaN, true };

        yield return new object[] { (RedisValue)true, true };
        yield return new object[] { (RedisValue)false, true };
        yield return new object[] { RedisValue.Null, true };
        yield return new object[] { RedisValue.EmptyString, true };
        yield return new object[] { (RedisValue)"abc", true };
        yield return new object[] { (RedisValue)s_abc, true };
        yield return new object[] { (RedisValue)new Memory<byte>(s_abc), false };
        yield return new object[] { (RedisValue)new ReadOnlyMemory<byte>(s_abc), false };
    }
}
