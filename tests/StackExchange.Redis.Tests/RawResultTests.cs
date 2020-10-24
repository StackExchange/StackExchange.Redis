using System.Buffers;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class RawResultTests
    {
        [Fact]
        public void TypeLoads()
        {
            var type = typeof(RawResult);
            Assert.Equal(nameof(RawResult), type.Name);
        }
        [Fact]
        public void NullWorks()
        {
            var result = new RawResult(ResultType.BulkString, ReadOnlySequence<byte>.Empty, true);
            Assert.Equal(ResultType.BulkString, result.Type);
            Assert.True(result.IsNull);

            var value = result.AsRedisValue();

            Assert.True(value.IsNull);
            string s = value;
            Assert.Null(s);

            byte[] arr = (byte[])value;
            Assert.Null(arr);
        }

        [Fact]
        public void DefaultWorks()
        {
            var result = default(RawResult);
            Assert.Equal(ResultType.None, result.Type);
            Assert.True(result.IsNull);

            var value = result.AsRedisValue();

            Assert.True(value.IsNull);
            var s = (string)value;
            Assert.Null(s);

            var arr = (byte[])value;
            Assert.Null(arr);
        }

        [Fact]
        public void NilWorks()
        {
            var result = RawResult.Nil;
            Assert.Equal(ResultType.None, result.Type);
            Assert.True(result.IsNull);

            var value = result.AsRedisValue();

            Assert.True(value.IsNull);
            var s = (string)value;
            Assert.Null(s);

            var arr = (byte[])value;
            Assert.Null(arr);
        }
    }
}
