using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace NRediSearch.Test
{
    public class ExampleUsage : RediSearchTestBase
    {
        public ExampleUsage(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void BasicUsage()
        {
            var client = GetClient();

            try { client.DropIndex(); } catch { } // reset DB

            // Defining a schema for an index and creating it:
            var sc = new Schema()
                .AddTextField("title", 5.0)
                .AddTextField("body", 1.0)
                .AddNumericField("price");

            bool result = false;
            try
            {
                result = client.CreateIndex(sc, Client.IndexOptions.Default);
            }
            catch (RedisServerException ex)
            {
                // TODO: Convert to Skip
                if (ex.Message == "ERR unknown command 'FT.CREATE'")
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine("Module not installed, aborting");
                }
                throw;
            }

            Assert.True(result);

            // note: using java API equivalent here; it would be nice to
            // use meta-programming / reflection instead in .NET

            // Adding documents to the index:
            var fields = new Dictionary<string, RedisValue>
            {
                ["title"] = "hello world",
                ["body"] = "lorem ipsum",
                ["price"] = 1337
            };

            Assert.True(client.AddDocument("doc1", fields));

            // Creating a complex query
            var q = new Query("hello world")
                .AddFilter(new Query.NumericFilter("price", 1300, 1350))
                .Limit(0, 5);

            // actual search
            var res = client.Search(q);

            Assert.Equal(1, res.TotalResults);
            var item = res.Documents.Single();
            Assert.Equal("doc1", item.Id);

            Assert.True(item.HasProperty("title"));
            Assert.True(item.HasProperty("body"));
            Assert.True(item.HasProperty("price"));
            Assert.False(item.HasProperty("blap"));

            Assert.Equal("hello world", (string)item["title"]);
            Assert.Equal("lorem ipsum", (string)item["body"]);
            Assert.Equal(1337, (int)item["price"]);
        }
    }
}
