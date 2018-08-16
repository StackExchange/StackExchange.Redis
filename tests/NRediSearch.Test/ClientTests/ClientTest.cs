using System;
using System.Collections.Generic;
using System.Text;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace NRediSearch.Test.ClientTests
{
    public class ClientTest : RediSearchTestBase
    {
        public ClientTest(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Search()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0).AddTextField("body", 1.0);

            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));
            var fields = new Dictionary<string, RedisValue>
            {
                { "title", "hello world" },
                { "body", "lorem ipsum" }
            };
            for (int i = 0; i < 100; i++)
            {
                Assert.True(cl.AddDocument($"doc{i}", fields, (double)i / 100.0));
            }

            SearchResult res = cl.Search(new Query("hello world") { WithScores = true }.Limit(0, 5));
            Assert.Equal(100, res.TotalResults);
            Assert.Equal(5, res.Documents.Count);
            foreach (var d in res.Documents)
            {
                Assert.StartsWith("doc", d.Id);
                Assert.True(d.Score < 100);
                //System.out.println(d);
            }

            Assert.True(cl.DeleteDocument("doc0"));
            Assert.False(cl.DeleteDocument("doc0"));

            res = cl.Search(new Query("hello world"));
            Assert.Equal(99, res.TotalResults);

            Assert.True(cl.DropIndex());

            var ex = Assert.Throws<RedisServerException>(() => cl.Search(new Query("hello world")));
            Assert.Equal("Unknown Index name", ex.Message);
        }

        [Fact]
        public void TestNumericFilter()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0).AddNumericField("price");

            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));

            for (int i = 0; i < 100; i++)
            {
                var fields = new Dictionary<string, RedisValue>
                {
                    { "title", "hello world" },
                    { "price", i }
                };
                Assert.True(cl.AddDocument($"doc{i}", fields));
            }

            SearchResult res = cl.Search(new Query("hello world").
                    AddFilter(new Query.NumericFilter("price", 0, 49)));
            Assert.Equal(50, res.TotalResults);
            Assert.Equal(10, res.Documents.Count);
            foreach (var d in res.Documents)
            {
                long price = (long)d["price"];
                Assert.True(price >= 0);
                Assert.True(price <= 49);
            }

            res = cl.Search(new Query("hello world").
                    AddFilter(new Query.NumericFilter("price", 0, true, 49, true)));
            Assert.Equal(48, res.TotalResults);
            Assert.Equal(10, res.Documents.Count);
            foreach (var d in res.Documents)
            {
                long price = (long)d["price"];
                Assert.True(price > 0);
                Assert.True(price < 49);
            }
            res = cl.Search(new Query("hello world").
                    AddFilter(new Query.NumericFilter("price", 50, 100)));
            Assert.Equal(50, res.TotalResults);
            Assert.Equal(10, res.Documents.Count);
            foreach (var d in res.Documents)
            {
                long price = (long)d["price"];
                Assert.True(price >= 50);
                Assert.True(price <= 100);
            }

            res = cl.Search(new Query("hello world").
                    AddFilter(new Query.NumericFilter("price", 20, double.PositiveInfinity)));
            Assert.Equal(80, res.TotalResults);
            Assert.Equal(10, res.Documents.Count);

            res = cl.Search(new Query("hello world").
                            AddFilter(new Query.NumericFilter("price", double.NegativeInfinity, 10)));
            Assert.Equal(11, res.TotalResults);
            Assert.Equal(10, res.Documents.Count);
        }

        [Fact]
        public void TestStopwords()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);

            Assert.True(cl.CreateIndex(sc,
                    Client.IndexOptions.Default.SetStopwords("foo", "bar", "baz")));

            var fields = new Dictionary<string, RedisValue>
            {
                { "title", "hello world foo bar" }
            };
            Assert.True(cl.AddDocument("doc1", fields));
            SearchResult res = cl.Search(new Query("hello world"));
            Assert.Equal(1, res.TotalResults);
            res = cl.Search(new Query("foo bar"));
            Assert.Equal(0, res.TotalResults);

            Reset(cl);

            Assert.True(cl.CreateIndex(sc,
                    Client.IndexOptions.Default | Client.IndexOptions.DisableStopWords));
            fields = new Dictionary<string, RedisValue>
            {
                { "title", "hello world foo bar to be or not to be" }
            };
            Assert.True(cl.AddDocument("doc1", fields));

            Assert.Equal(1, cl.Search(new Query("hello world")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("foo bar")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("to be or not to be")).TotalResults);
        }

        [Fact]
        public void TestGeoFilter()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0).AddGeoField("loc");

            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));
            var fields = new Dictionary<string, RedisValue>
            {
                { "title", "hello world" },
                { "loc", "-0.441,51.458" }
            };
            Assert.True(cl.AddDocument("doc1", fields));

            fields["loc"] = "-0.1,51.2";
            Assert.True(cl.AddDocument("doc2", fields));

            SearchResult res = cl.Search(new Query("hello world").
                    AddFilter(
                            new Query.GeoFilter("loc", -0.44, 51.45,
                                    10, GeoUnit.Kilometers)
                    ));

            Assert.Equal(1, res.TotalResults);
            res = cl.Search(new Query("hello world").
                            AddFilter(
                                    new Query.GeoFilter("loc", -0.44, 51.45,
                                            100, GeoUnit.Kilometers)
                            ));
            Assert.Equal(2, res.TotalResults);
        }

        [Fact]
        public void TestPayloads()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);

            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));

            var fields = new Dictionary<string, RedisValue>
            {
                { "title", "hello world" }
            };
            const string payload = "foo bar";
            Assert.True(cl.AddDocument("doc1", fields, 1.0, false, false, Encoding.UTF8.GetBytes(payload)));
            SearchResult res = cl.Search(new Query("hello world") { WithPayloads = true });
            Assert.Equal(1, res.TotalResults);
            Assert.Single(res.Documents);

            Assert.Equal(payload, Encoding.UTF8.GetString(res.Documents[0].Payload));
        }

        [Fact]
        public void TestQueryFlags()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);

            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));
            var fields = new Dictionary<string, RedisValue>();

            for (int i = 0; i < 100; i++)
            {
                fields["title"] = i % 2 == 1 ? "hello worlds" : "hello world";
                Assert.True(cl.AddDocument($"doc{i}", fields, (double)i / 100.0));
            }

            Query q = new Query("hello").SetWithScores();
            SearchResult res = cl.Search(q);

            Assert.Equal(100, res.TotalResults);
            Assert.Equal(10, res.Documents.Count);

            foreach (var d in res.Documents)
            {
                Assert.StartsWith("doc", d.Id);
                Assert.True(d.Score != 1.0);
                Assert.StartsWith("hello world", (string)d["title"]);
            }

            q = new Query("hello").SetNoContent();
            res = cl.Search(q);
            foreach (var d in res.Documents)
            {
                Assert.StartsWith("doc", d.Id);
                Assert.True(d.Score == 1.0);
                Assert.True(d["title"].IsNull);
            }

            // test verbatim vs. stemming
            res = cl.Search(new Query("hello worlds"));
            Assert.Equal(100, res.TotalResults);
            res = cl.Search(new Query("hello worlds").SetVerbatim());
            Assert.Equal(50, res.TotalResults);

            res = cl.Search(new Query("hello a world").SetVerbatim());
            Assert.Equal(50, res.TotalResults);
            res = cl.Search(new Query("hello a worlds").SetVerbatim());
            Assert.Equal(50, res.TotalResults);
            res = cl.Search(new Query("hello a world").SetVerbatim().SetNoStopwords());
            Assert.Equal(0, res.TotalResults);
        }

        [Fact]
        public void TestSortQueryFlags()
        {
            Client cl = GetClient();
            Schema sc = new Schema().AddSortableTextField("title", 1.0);

            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));
            var fields = new Dictionary<string, RedisValue>
            {
                ["title"] = "b title"
            };
            cl.AddDocument("doc1", fields, 1.0, false, true, null);

            fields["title"] = "a title";
            cl.AddDocument("doc2", fields, 1.0, false, true, null);

            fields["title"] = "c title";
            cl.AddDocument("doc3", fields, 1.0, false, true, null);

            Query q = new Query("title").SetSortBy("title", true);
            SearchResult res = cl.Search(q);

            Assert.Equal(3, res.TotalResults);
            Document doc1 = res.Documents[0];
            Assert.Equal("a title", doc1["title"]);

            doc1 = res.Documents[1];
            Assert.Equal("b title", doc1["title"]);

            doc1 = res.Documents[2];
            Assert.Equal("c title", doc1["title"]);
        }

        [Fact]
        public void TestAddHash()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);
            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));
            RedisKey hashKey = (string)cl.IndexName + ":foo";
            Db.KeyDelete(hashKey);
            Db.HashSet(hashKey, "title", "hello world");

            Assert.True(cl.AddHash(hashKey, 1, false));
            SearchResult res = cl.Search(new Query("hello world").SetVerbatim());
            Assert.Equal(1, res.TotalResults);
            Assert.Equal(hashKey, res.Documents[0].Id);
        }

        [Fact]
        public void TestDrop()
        {
            Client cl = GetClient();
            Db.Execute("FLUSHDB"); // yeah, this is horrible, deal with it

            Schema sc = new Schema().AddTextField("title", 1.0);

            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));
            var fields = new Dictionary<string, RedisValue>
            {
                { "title", "hello world" }
            };
            for (int i = 0; i < 100; i++)
            {
                Assert.True(cl.AddDocument($"doc{i}", fields));
            }

            SearchResult res = cl.Search(new Query("hello world"));
            Assert.Equal(100, res.TotalResults);

            var key = (string)Db.KeyRandom();
            Assert.NotNull(key);

            Reset(cl);

            key = (string)Db.KeyRandom();
            Assert.Null(key);
        }

        [Fact]
        public void TestNoStem()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("stemmed", 1.0).AddField(new Schema.TextField("notStemmed", 1.0, false, true));
            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));

            var doc = new Dictionary<string, RedisValue>
            {
                { "stemmed", "located" },
                { "notStemmed", "located" }
            };
            // Store it
            Assert.True(cl.AddDocument("doc", doc));

            // Query
            SearchResult res = cl.Search(new Query("@stemmed:location"));
            Assert.Equal(1, res.TotalResults);

            res = cl.Search(new Query("@notStemmed:location"));
            Assert.Equal(0, res.TotalResults);
        }

        [Fact]
        public void TestInfo()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);
            Assert.True(cl.CreateIndex(sc, Client.IndexOptions.Default));

            var info = cl.GetInfo();
            Assert.Equal((string)cl.IndexName, (string)info["index_name"]);
        }

        [Fact]
        public void TestNoIndex()
        {
            Client cl = GetClient();

            Schema sc = new Schema()
                        .AddField(new Schema.TextField("f1", 1.0, true, false, true))
                        .AddField(new Schema.TextField("f2", 1.0));
            cl.CreateIndex(sc, Client.IndexOptions.Default);

            var mm = new Dictionary<string, RedisValue>
            {
                { "f1", "MarkZZ" },
                { "f2", "MarkZZ" }
            };
            cl.AddDocument("doc1", mm);

            mm.Clear();
            mm.Add("f1", "MarkAA");
            mm.Add("f2", "MarkBB");
            cl.AddDocument("doc2", mm);

            SearchResult res = cl.Search(new Query("@f1:Mark*"));
            Assert.Equal(0, res.TotalResults);

            res = cl.Search(new Query("@f2:Mark*"));
            Assert.Equal(2, res.TotalResults);

            res = cl.Search(new Query("@f2:Mark*").SetSortBy("f1", false));
            Assert.Equal(2, res.TotalResults);

            Assert.Equal("doc1", res.Documents[0].Id);

            res = cl.Search(new Query("@f2:Mark*").SetSortBy("f1", true));
            Assert.Equal("doc2", res.Documents[0].Id);
        }

        [Fact]
        public void TestReplacePartial()
        {
            Client cl = GetClient();

            Schema sc = new Schema()
                        .AddTextField("f1", 1.0)
                        .AddTextField("f2", 1.0)
                        .AddTextField("f3", 1.0);
            cl.CreateIndex(sc, Client.IndexOptions.Default);

            var mm = new Dictionary<string, RedisValue>
            {
                { "f1", "f1_val" },
                { "f2", "f2_val" }
            };

            cl.AddDocument("doc1", mm);
            cl.AddDocument("doc2", mm);

            mm.Clear();
            mm.Add("f3", "f3_val");

            cl.UpdateDocument("doc1", mm, 1.0);
            cl.ReplaceDocument("doc2", mm, 1.0);

            // Search for f3 value. All documents should have it.
            SearchResult res = cl.Search(new Query("@f3:f3_Val"));
            Assert.Equal(2, res.TotalResults);

            res = cl.Search(new Query("@f3:f3_val @f2:f2_val @f1:f1_val"));
            Assert.Equal(1, res.TotalResults);
        }

        [Fact]
        public void TestExplain()
        {
            Client cl = GetClient();

            Schema sc = new Schema()
                        .AddTextField("f1", 1.0)
                        .AddTextField("f2", 1.0)
                        .AddTextField("f3", 1.0);
            cl.CreateIndex(sc, Client.IndexOptions.Default);

            var res = cl.Explain(new Query("@f3:f3_val @f2:f2_val @f1:f1_val"));
            Assert.NotNull(res);
            Assert.False(res.Length == 0);
            Output.WriteLine(res);
        }

        [Fact]
        public void TestHighlightSummarize()
        {
            Client cl = GetClient();
            Schema sc = new Schema().AddTextField("text", 1.0);
            cl.CreateIndex(sc, Client.IndexOptions.Default);

            var doc = new Dictionary<string, RedisValue>
            {
                { "text", "Redis is often referred as a data structures server. What this means is that Redis provides access to mutable data structures via a set of commands, which are sent using a server-client model with TCP sockets and a simple protocol. So different processes can query and modify the same data structures in a shared way" }
            };
            // Add a document
            cl.AddDocument("foo", doc, 1.0);
            Query q = new Query("data").HighlightFields().SummarizeFields();
            SearchResult res = cl.Search(q);

            Assert.Equal("is often referred as a <b>data</b> structures server. What this means is that Redis provides... What this means is that Redis provides access to mutable <b>data</b> structures via a set of commands, which are sent using a... So different processes can query and modify the same <b>data</b> structures in a shared... ",
                    res.Documents[0]["text"]);
        }

        [Fact]
        public void TestLanguage()
        {
            Client cl = GetClient();
            Schema sc = new Schema().AddTextField("text", 1.0);
            cl.CreateIndex(sc, Client.IndexOptions.Default);

            Document d = new Document("doc1").Set("text", "hello");
            AddOptions options = new AddOptions().SetLanguage("spanish");
            Assert.True(cl.AddDocument(d, options));

            options.SetLanguage("ybreski");
            cl.DeleteDocument(d.Id);

            var ex = Assert.Throws<RedisServerException>(() => cl.AddDocument(d, options));
            Assert.Equal("Unsupported Language", ex.Message);
        }

        [Fact]
        public void TestDropMissing()
        {
            Client cl = GetClient();
            var ex = Assert.Throws<RedisServerException>(() => cl.DropIndex());
            Assert.Equal("Unknown Index name", ex.Message);
        }

        [Fact]
        public void TestGet()
        {
            Client cl = GetClient();
            cl.CreateIndex(new Schema().AddTextField("txt1", 1.0), Client.IndexOptions.Default);
            cl.AddDocument(new Document("doc1").Set("txt1", "Hello World!"), new AddOptions());
            Document d = cl.GetDocument("doc1");
            Assert.NotNull(d);
            Assert.Equal("Hello World!", d["txt1"]);

            // Get something that does not exist. Shouldn't explode
            Assert.Null(cl.GetDocument("nonexist"));
        }
    }
}
