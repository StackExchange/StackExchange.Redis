using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests
{
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
            object x = value.Box(), y = value.Box();
            Assert.Equal(expectSameReference, ReferenceEquals(x, y));
            // check we got the right values!
            AssertEqualGiveOrTakeNaN(value, RedisValue.Unbox(x));
            AssertEqualGiveOrTakeNaN(value, RedisValue.Unbox(y));
        }

        static void AssertEqualGiveOrTakeNaN(RedisValue expected, RedisValue actual)
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
            => new []
            {
                new object[] { RedisValue.Null },
                new object[] { RedisValue.EmptyString },
                new object[] { (RedisValue)0L },
                new object[] { (RedisValue)1L },
                new object[] { (RedisValue)18L },
                new object[] { (RedisValue)19L },
                new object[] { (RedisValue)20L },
                new object[] { (RedisValue)21L },
                new object[] { (RedisValue)22L },
                new object[] { (RedisValue)(-1L) },
                new object[] { (RedisValue)0 },
                new object[] { (RedisValue)1 },
                new object[] { (RedisValue)18 },
                new object[] { (RedisValue)19 },
                new object[] { (RedisValue)20 },
                new object[] { (RedisValue)21 },
                new object[] { (RedisValue)22 },
                new object[] { (RedisValue)(-1) },
                new object[] { (RedisValue)0F },
                new object[] { (RedisValue)1F },
                new object[] { (RedisValue)(-1F) },
                new object[] { (RedisValue)0D },
                new object[] { (RedisValue)1D },
                new object[] { (RedisValue)(-1D) },
                new object[] { (RedisValue)float.PositiveInfinity },
                new object[] { (RedisValue)float.NegativeInfinity },
                new object[] { (RedisValue)float.NaN },
                new object[] { (RedisValue)double.PositiveInfinity },
                new object[] { (RedisValue)double.NegativeInfinity },
                new object[] { (RedisValue)double.NaN },
                new object[] { (RedisValue)true },
                new object[] { (RedisValue)false },
                new object[] { (RedisValue)(string)null },
                new object[] { (RedisValue)"abc" },
                new object[] { (RedisValue)s_abc },
                new object[] { (RedisValue)new Memory<byte>(s_abc) },
                new object[] { (RedisValue)new ReadOnlyMemory<byte>(s_abc) },
            };

        public static IEnumerable<object[]> UnboxValues
            => new []
            {
                new object[] { null, RedisValue.Null },
                new object[] { "", RedisValue.EmptyString },
                new object[] { 0, (RedisValue)0 },
                new object[] { 1, (RedisValue)1 },
                new object[] { 18, (RedisValue)18 },
                new object[] { 19, (RedisValue)19 },
                new object[] { 20, (RedisValue)20 },
                new object[] { 21, (RedisValue)21 },
                new object[] { 22, (RedisValue)22 },
                new object[] { -1, (RedisValue)(-1) },
                new object[] { 18L, (RedisValue)18 },
                new object[] { 19L, (RedisValue)19 },
                new object[] { 20L, (RedisValue)20 },
                new object[] { 21L, (RedisValue)21 },
                new object[] { 22L, (RedisValue)22 },
                new object[] { -1L, (RedisValue)(-1) },
                new object[] { 0F, (RedisValue)0 },
                new object[] { 1F, (RedisValue)1 },
                new object[] { -1F, (RedisValue)(-1) },
                new object[] { 0D, (RedisValue)0 },
                new object[] { 1D, (RedisValue)1 },
                new object[] { -1D, (RedisValue)(-1) },
                new object[] { float.PositiveInfinity, (RedisValue)double.PositiveInfinity },
                new object[] { float.NegativeInfinity, (RedisValue)double.NegativeInfinity },
                new object[] { float.NaN, (RedisValue)double.NaN },
                new object[] { double.PositiveInfinity, (RedisValue)double.PositiveInfinity },
                new object[] { double.NegativeInfinity, (RedisValue)double.NegativeInfinity },
                new object[] { double.NaN, (RedisValue)double.NaN },
                new object[] { true, (RedisValue)true },
                new object[] { false, (RedisValue)false},
                new object[] { "abc", (RedisValue)"abc" },
                new object[] { s_abc, (RedisValue)s_abc },
                new object[] { new Memory<byte>(s_abc), (RedisValue)s_abc },
                new object[] { new ReadOnlyMemory<byte>(s_abc), (RedisValue)s_abc },
                new object[] { (RedisValue)1234, (RedisValue)1234 },
            };

        public static IEnumerable<object[]> InternedValues()
        {
            for(int i = -20; i <= 40; i++)
            {
                bool expectInterned = i >= -1 & i <= 20;
                yield return new object[] { (RedisValue)i, expectInterned };
                yield return new object[] { (RedisValue)(long)i, expectInterned };
                yield return new object[] { (RedisValue)(float)i, expectInterned };
                yield return new object[] { (RedisValue)(double)i, expectInterned };
            }

            yield return new object[] { (RedisValue)float.NegativeInfinity, true };
            yield return new object[] { (RedisValue)(-0.5F), false };
            yield return new object[] { (RedisValue)(0.5F), false };
            yield return new object[] { (RedisValue)float.PositiveInfinity, true };
            yield return new object[] { (RedisValue)float.NaN, true };

            yield return new object[] { (RedisValue)double.NegativeInfinity, true };
            yield return new object[] { (RedisValue)(-0.5D), false };
            yield return new object[] { (RedisValue)(0.5D), false };
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
}
