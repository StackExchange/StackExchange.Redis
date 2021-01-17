using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class QueryStringTests : TestBase
    {
        public QueryStringTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("?")]
        [InlineData("? ")]
        [InlineData("\n")]
        [InlineData("?\n")]
        [InlineData("\r\n")]
        [InlineData("?\r\n")]
        [InlineData("\t")]
        [InlineData("?\t")]
        public void EmptyQueryString(string qs)
        {
            var collection = new QueryStringDictionary(qs);
            Assert.Empty(collection);
        }


        [Fact]
        public void ParseDecodedQueryString_SortedValues()
        {
            var collection = new QueryStringDictionary("?b=1&c=2&c=1&d=a&d&e");

            Assert.NotEmpty(collection);
            Assert.Equal(new List<string> { "1" }, collection["b"]);
            Assert.Equal(new List<string> { "1", "2" }, collection["c"]);
            Assert.Equal(new List<string> { "", "a" }, collection["d"]);
            Assert.Equal(new List<string>() { "" }, collection["e"]);
        }

        [Fact]
        public void ParseEncodedQueryString_DecodeKeysAndValues()
        {
            var collection = new QueryStringDictionary("?lang=f%23&lang=c%23&c%2B%2B");

            Assert.NotEmpty(collection);
            Assert.Equal(new List<string>() { "c#", "f#" }, collection["lang"]);
            Assert.Equal(new List<string>() { "" }, collection["c++"]);
        }


        [Theory]
        [InlineData("", "", false)]
        [InlineData("", "", true)]
        [InlineData("?b=2&c=2&c=1&d=a&d&e", "?b=2&c=1&c=2&d&d=a&e", false)]
        [InlineData("?lang=f%23&lang=c%23&c%2B%2B", "?c%2B%2B&lang=c%23&lang=f%23", true)]
        [InlineData("?lang=f%23&lang=c%23&c%2B%2B", "?c++&lang=c#&lang=f#", false)]
        public void QueryStringToString_SortedValuesInOutput(string input, string expected, bool encode)
        {
            var collection = new QueryStringDictionary(input);

            var actualString = collection.ToString(encode);

            Assert.Equal(expected, actualString);
        }
    }
}
