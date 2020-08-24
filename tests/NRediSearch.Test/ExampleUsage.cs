using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;
using static NRediSearch.Client;

namespace NRediSearch.Test
{
    public class ExampleUsage : RediSearchTestBase
    {
        public ExampleUsage(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void BasicUsage()
        {
            var client = GetClient();

            try { client.DropIndex(); } catch { /* Intentionally ignored */ } // reset DB

            // Defining a schema for an index and creating it:
            var sc = new Schema()
                .AddTextField("title", 5.0)
                .AddTextField("body", 1.0)
                .AddNumericField("price");

            bool result = false;
            try
            {
                result = client.CreateIndex(sc, new ConfiguredIndexOptions());
            }
            catch (RedisServerException ex)
            {
                // TODO: Convert to Skip
                if (ex.Message == "ERR unknown command 'FT.CREATE'")
                {
                    Output.WriteLine(ex.Message);
                    Output.WriteLine("Module not installed, aborting");
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

            Assert.Equal("hello world", item["title"]);
            Assert.Equal("lorem ipsum", item["body"]);
            Assert.Equal(1337, (int)item["price"]);
        }

        [Fact]
        public void BasicScoringUsage()
        {
            var client = GetClient();

            try { client.DropIndex(); } catch { /* Intentionally ignored */ } // reset DB

            CreateSchema(client);

            var term = "petit*";

            var query = new Query(term);
            query.Limit(0, 10);
            query.WithScores = true;

            var searchResult = client.Search(query);

            var docResult = searchResult.Documents.FirstOrDefault();

            Assert.Equal(1, searchResult.TotalResults);
            Assert.NotEqual(0, docResult.Score);
            Assert.Equal("1", docResult.Id);
            Assert.Null(docResult.ScoreExplained);
        }

        [Fact]
        public void BasicScoringUsageWithExplainScore()
        {
            var client = GetClient();

            try { client.DropIndex(); } catch { /* Intentionally ignored */ } // reset DB

            CreateSchema(client);

            var term = "petit*";

            var query = new Query(term);
            query.Limit(0, 10);
            query.WithScores = true;
            query.Scoring = "TFIDF";
            query.ExplainScore = true;

            var searchResult = client.Search(query);

            var docResult = searchResult.Documents.FirstOrDefault();

            Assert.Equal(1, searchResult.TotalResults);
            Assert.NotEqual(0, docResult.Score);
            Assert.Equal("1", docResult.Id);
            Assert.NotEmpty(docResult.ScoreExplained);
            Assert.Equal("Final TFIDF : words TFIDF 1.00 * document score 1.00 / norm 2 / slop 1", docResult.ScoreExplained[0]);
            Assert.Equal("(Weight 1.00 * total children TFIDF 1.00)", docResult.ScoreExplained[1]);
            Assert.Equal("(TFIDF 1.00 = Weight 1.00 * TF 1 * IDF 1.00)", docResult.ScoreExplained[2]);
        }

        [Fact]
        public void BasicScoringUsageWithExplainScoreDifferentScorer()
        {
            var client = GetClient();

            try { client.DropIndex(); } catch { /* Intentionally ignored */ } // reset DB

            CreateSchema(client);

            var term = "petit*";

            var query = new Query(term);
            query.Limit(0, 10);
            query.WithScores = true;
            query.Scoring = "TFIDF.DOCNORM";
            query.ExplainScore = true;

            var searchResult = client.Search(query);

            var docResult = searchResult.Documents.FirstOrDefault();

            Assert.Equal(1, searchResult.TotalResults);
            Assert.NotEqual(0, docResult.Score);
            Assert.Equal("1", docResult.Id);
            Assert.NotEmpty(docResult.ScoreExplained);
            Assert.Equal("Final TFIDF : words TFIDF 1.00 * document score 1.00 / norm 20 / slop 1", docResult.ScoreExplained[0]);
            Assert.Equal("(Weight 1.00 * total children TFIDF 1.00)", docResult.ScoreExplained[1]);
            Assert.Equal("(TFIDF 1.00 = Weight 1.00 * TF 1 * IDF 1.00)", docResult.ScoreExplained[2]);
        }

        private void CreateSchema(Client client)
        {
            var schema = new Schema();

            schema
                .AddSortableTextField("title")
                .AddTextField("country")
                .AddTextField("author")
                .AddTextField("aka")
                .AddTagField("language");

            client.CreateIndex(schema, new ConfiguredIndexOptions());

            var doc = new Document("1");

            doc
                .Set("title", "Le Petit Prince")
                .Set("country", "France")
                .Set("author", "Antoine de Saint-Exupéry")
                .Set("language", "fr_FR")
                .Set("aka", "The Little Prince, El Principito");

            client.AddDocument(doc);
        }
    }
}

