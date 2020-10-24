using System.Collections.Generic;
using System.Text;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;
using static NRediSearch.Client;
using static NRediSearch.Schema;
using static NRediSearch.SuggestionOptions;

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

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));
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
            Output.WriteLine("Exception: " + ex.Message);
            Assert.True(IsMissingIndexException(ex));
        }

        [Fact]
        public void TestNumericFilter()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0).AddNumericField("price");

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));

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

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions().SetStopwords("foo", "bar", "baz")));

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

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions().SetNoStopwords()));
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

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));
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

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));

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

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));
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
                Assert.StartsWith("hello world", d["title"]);
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

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));
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
        public void TestIndexDefinition()
        {
            Client cl = GetClient();
            Schema sc = new Schema().AddTextField("title", 1.0);
            ConfiguredIndexOptions options = new ConfiguredIndexOptions(
                new IndexDefinition( prefixes: new string[]{cl.IndexName}));
            Assert.True(cl.CreateIndex(sc, options));

            RedisKey hashKey = (string)cl.IndexName + ":foo";
            Db.KeyDelete(hashKey);
            Db.HashSet(hashKey, "title", "hello world");

            try
            {
#pragma warning disable 0618
                Assert.True(cl.AddHash(hashKey, 1, false));
#pragma warning restore 0618
            }
            catch (RedisServerException e)
            {
                Assert.StartsWith("ERR unknown command `FT.ADDHASH`", e.Message);
                return; // Starting from RediSearch 2.0 this command is not supported anymore
            }
            SearchResult res = cl.Search(new Query("hello world").SetVerbatim());
            Assert.Equal(1, res.TotalResults);
            Assert.Equal(hashKey, res.Documents[0].Id);
        }

        [Fact]
        public void TestDrop()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));
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
            Output.WriteLine("Found key: " + key);
            Assert.NotNull(key);

            Reset(cl);

            var indexExists = Db.KeyExists(cl.IndexName);
            Assert.False(indexExists);
        }

        [Fact]
        public void TestAlterAdd()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));
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

            Assert.True(cl.AlterIndex(new TagField("tags", ","), new TextField("name", 0.5)));
            for (int i = 0; i < 100; i++)
            {
                var fields2 = new Dictionary<string, RedisValue>();
                fields2.Add("name", $"name{i}");
                fields2.Add("tags", $"tagA,tagB,tag{i}");
                Assert.True(cl.UpdateDocument($"doc{i}", fields2, 1.0));
            }
            SearchResult res2 = cl.Search(new Query("@tags:{tagA}"));
            Assert.Equal(100, res2.TotalResults);

            var info = cl.GetInfoParsed();
            Assert.Equal(cl.IndexName, info.IndexName);

            Assert.True(info.Fields.ContainsKey("tags"));
            Assert.Equal("TAG", (string)info.Fields["tags"][2]);
        }

        [Fact]
        public void TestNoStem()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("stemmed", 1.0).AddField(new TextField("notStemmed", 1.0, false, true));
            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));

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
        public void TestInfoParsed()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);
            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));

            var info = cl.GetInfoParsed();
            Assert.Equal(cl.IndexName, info.IndexName);
        }

        [Fact]
        public void TestInfo()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("title", 1.0);
            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));

            var info = cl.GetInfo();
            Assert.Equal(cl.IndexName, info["index_name"]);
        }

        [Fact]
        public void TestNoIndex()
        {
            Client cl = GetClient();

            Schema sc = new Schema()
                        .AddField(new TextField("f1", 1.0, true, false, true))
                        .AddField(new TextField("f2", 1.0));
            cl.CreateIndex(sc, new ConfiguredIndexOptions());

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
            cl.CreateIndex(sc, new ConfiguredIndexOptions());

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
            cl.CreateIndex(sc, new ConfiguredIndexOptions());

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
            cl.CreateIndex(sc, new ConfiguredIndexOptions());

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

            q = new Query("data").HighlightFields(new Query.HighlightTags("<u>", "</u>")).SummarizeFields();
            res = cl.Search(q);

            Assert.Equal("is often referred as a <u>data</u> structures server. What this means is that Redis provides... What this means is that Redis provides access to mutable <u>data</u> structures via a set of commands, which are sent using a... So different processes can query and modify the same <u>data</u> structures in a shared... ",
                res.Documents[0]["text"]);
        }

        [Fact]
        public void TestLanguage()
        {
            Client cl = GetClient();
            Schema sc = new Schema().AddTextField("text", 1.0);
            cl.CreateIndex(sc, new ConfiguredIndexOptions());

            Document d = new Document("doc1").Set("text", "hello");
            AddOptions options = new AddOptions().SetLanguage("spanish");
            Assert.True(cl.AddDocument(d, options));

            options.SetLanguage("ybreski");
            cl.DeleteDocument(d.Id);

            var ex = Assert.Throws<RedisServerException>(() => cl.AddDocument(d, options));
            Assert.Equal("Unsupported language", ex.Message, ignoreCase: true);
        }

        [Fact]
        public void TestDropMissing()
        {
            Client cl = GetClient();
            var ex = Assert.Throws<RedisServerException>(() => cl.DropIndex());
            Assert.True(IsMissingIndexException(ex));
        }

        [Fact]
        public void TestGet()
        {
            Client cl = GetClient();
            cl.CreateIndex(new Schema().AddTextField("txt1", 1.0), new ConfiguredIndexOptions());
            cl.AddDocument(new Document("doc1").Set("txt1", "Hello World!"), new AddOptions());
            Document d = cl.GetDocument("doc1");
            Assert.NotNull(d);
            Assert.Equal("Hello World!", d["txt1"]);

            // Get something that does not exist. Shouldn't explode
            Assert.Null(cl.GetDocument("nonexist"));
        }

        [Fact]
        public void TestMGet()
        {
            Client cl = GetClient();

            cl.CreateIndex(new Schema().AddTextField("txt1", 1.0), new ConfiguredIndexOptions());
            cl.AddDocument(new Document("doc1").Set("txt1", "Hello World!1"), new AddOptions());
            cl.AddDocument(new Document("doc2").Set("txt1", "Hello World!2"), new AddOptions());
            cl.AddDocument(new Document("doc3").Set("txt1", "Hello World!3"), new AddOptions());

            var docs = cl.GetDocuments();
            Assert.Empty(docs);

            docs = cl.GetDocuments("doc1", "doc3", "doc4");
            Assert.Equal(3, docs.Length);
            Assert.Equal("Hello World!1", docs[0]["txt1"]);
            Assert.Equal("Hello World!3", docs[1]["txt1"]);
            Assert.Null(docs[2]);
        }

        [Fact]
        public void TestAddSuggestionGetSuggestionFuzzy()
        {
            Client cl = GetClient();
            Suggestion suggestion = Suggestion.Builder.String("TOPIC OF WORDS").Score(1).Build();
            // test can add a suggestion string
            Assert.True(cl.AddSuggestion(suggestion, true) > 0, $"{suggestion} insert should of returned at least 1");
            // test that the partial part of that string will be returned using fuzzy

            //Assert.Equal(suggestion.ToString() + " suppose to be returned", suggestion, cl.GetSuggestion(suggestion.String.Substring(0, 3), SuggestionOptions.GetBuilder().Build()).get(0));
            Assert.Equal(suggestion.ToString(), cl.GetSuggestions(suggestion.String.Substring(0, 3), SuggestionOptions.Builder.Build())[0].ToString());
        }

        [Fact]
        public void TestAddSuggestionGetSuggestion()
        {
            Client cl = GetClient();
            Suggestion suggestion = Suggestion.Builder.String("ANOTHER_WORD").Score(1).Build();
            Suggestion noMatch = Suggestion.Builder.String("_WORD MISSED").Score(1).Build();

            Assert.True(cl.AddSuggestion(suggestion, false) > 0, $"{suggestion} should of inserted at least 1");
            Assert.True(cl.AddSuggestion(noMatch, false) > 0, $"{noMatch} should of inserted at least 1");

            // test that with a partial part of that string will have the entire word returned SuggestionOptions.builder().build()
            Assert.Single(cl.GetSuggestions(suggestion.String.Substring(0, 3), SuggestionOptions.Builder.Fuzzy().Build()));

            // turn off fuzzy start at second word no hit
            Assert.Empty(cl.GetSuggestions(noMatch.String.Substring(1, 6), SuggestionOptions.Builder.Build()));
            // my attempt to trigger the fuzzy by 1 character
            Assert.Single(cl.GetSuggestions(noMatch.String.Substring(1, 6), SuggestionOptions.Builder.Fuzzy().Build()));
        }

        [Fact]
        public void TestAddSuggestionGetSuggestionPayloadScores()
        {
            Client cl = GetClient();

            Suggestion suggestion = Suggestion.Builder.String("COUNT_ME TOO").Payload("PAYLOADS ROCK ").Score(0.2).Build();
            Assert.True(cl.AddSuggestion(suggestion, false) > 0, $"{suggestion} insert should of at least returned 1");
            Assert.True(cl.AddSuggestion(suggestion.ToBuilder().String("COUNT").Payload("My PAYLOAD is better").Build(), false) > 1, "Count single added should return more than 1");
            Assert.True(cl.AddSuggestion(suggestion.ToBuilder().String("COUNT_ANOTHER").Score(1).Payload(null).Build(), false) > 1, "Count single added should return more than 1");

            Suggestion noScoreOrPayload = Suggestion.Builder.String("COUNT NO PAYLOAD OR COUNT").Build();
            Assert.True(cl.AddSuggestion(noScoreOrPayload, true) > 1, "Count single added should return more than 1");

            var payloads = cl.GetSuggestions(suggestion.String.Substring(0, 3), SuggestionOptions.Builder.With(WithOptions.PayloadsAndScores).Build());
            Assert.Equal(4, payloads.Length);
            Assert.True(payloads[2].Payload.Length > 0);
            Assert.True(payloads[1].Score < .299, "Actual score: " + payloads[1].Score);
        }

        [Fact]
        public void TestAddSuggestionGetSuggestionPayload()
        {
            Client cl = GetClient();
            cl.AddSuggestion(Suggestion.Builder.String("COUNT_ME TOO").Payload("PAYLOADS ROCK ").Build(), false);
            cl.AddSuggestion(Suggestion.Builder.String("COUNT").Payload("ANOTHER PAYLOAD ").Build(), false);
            cl.AddSuggestion(Suggestion.Builder.String("COUNTNO PAYLOAD OR COUNT").Build(), false);

            // test that with a partial part of that string will have the entire word returned
            var payloads = cl.GetSuggestions("COU", SuggestionOptions.Builder.Max(3).Fuzzy().With(WithOptions.Payloads).Build());
            Assert.Equal(3, payloads.Length);
        }

        [Fact]
        public void TestGetSuggestionNoPayloadTwoOnly()
        {
            Client cl = GetClient();

            cl.AddSuggestion(Suggestion.Builder.String("DIFF_WORD").Score(0.4).Payload("PAYLOADS ROCK ").Build(), false);
            cl.AddSuggestion(Suggestion.Builder.String("DIFF wording").Score(0.5).Payload("ANOTHER PAYLOAD ").Build(), false);
            cl.AddSuggestion(Suggestion.Builder.String("DIFFERENT").Score(0.7).Payload("I am a payload").Build(), false);

            var payloads = cl.GetSuggestions("DIF", SuggestionOptions.Builder.Max(2).Build());
            Assert.Equal(2, payloads.Length);

            var three = cl.GetSuggestions("DIF", SuggestionOptions.Builder.Max(3).Build());
            Assert.Equal(3, three.Length);
        }

        [Fact]
        public void TestGetSuggestionsAsStringArray()
        {
            Client cl = GetClient();

            cl.AddSuggestion(Suggestion.Builder.String("DIFF_WORD").Score(0.4).Payload("PAYLOADS ROCK ").Build(), false);
            cl.AddSuggestion(Suggestion.Builder.String("DIFF wording").Score(0.5).Payload("ANOTHER PAYLOAD ").Build(), false);
            cl.AddSuggestion(Suggestion.Builder.String("DIFFERENT").Score(0.7).Payload("I am a payload").Build(), false);

            var payloads = cl.GetSuggestions("DIF", max: 2);
            Assert.Equal(2, payloads.Length);

            var three = cl.GetSuggestions("DIF", max: 3);
            Assert.Equal(3, three.Length);
        }

        [Fact]
        public void TestGetSuggestionWithScore()
        {
            Client cl = GetClient();

            cl.AddSuggestion(Suggestion.Builder.String("DIFF_WORD").Score(0.4).Payload("PAYLOADS ROCK ").Build(), true);
            var list = cl.GetSuggestions("DIF", SuggestionOptions.Builder.Max(2).With(WithOptions.Scores).Build());
            Assert.True(list[0].Score <= .2, "Actual score: " + list[0].Score);
        }

        [Fact]
        public void TestGetSuggestionAllNoHit()
        {
            Client cl = GetClient();

            cl.AddSuggestion(Suggestion.Builder.String("NO WORD").Score(0.4).Build(), false);

            var none = cl.GetSuggestions("DIF", SuggestionOptions.Builder.Max(3).With(WithOptions.Scores).Build());
            Assert.Empty(none);
        }

        [Fact]
        public void TestGetTagField()
        {
            Client cl = GetClient();
            Schema sc = new Schema()
                    .AddTextField("title", 1.0)
                    .AddTagField("category");

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));

            var search = cl.Search(new Query("hello"));
            Output.WriteLine("Initial search: " + search.TotalResults);
            Assert.Equal(0, search.TotalResults);

            var fields1 = new Dictionary<string, RedisValue>();
            fields1.Add("title", "hello world");
            fields1.Add("category", "red");
            Assert.True(cl.AddDocument("foo", fields1));
            var fields2 = new Dictionary<string, RedisValue>();
            fields2.Add("title", "hello world");
            fields2.Add("category", "blue");
            Assert.True(cl.AddDocument("bar", fields2));
            var fields3 = new Dictionary<string, RedisValue>();
            fields3.Add("title", "hello world");
            fields3.Add("category", "green,yellow");
            Assert.True(cl.AddDocument("baz", fields3));
            var fields4 = new Dictionary<string, RedisValue>();
            fields4.Add("title", "hello world");
            fields4.Add("category", "orange;purple");
            Assert.True(cl.AddDocument("qux", fields4));

            Assert.Equal(1, cl.Search(new Query("@category:{red}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("@category:{blue}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("hello @category:{red}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("hello @category:{blue}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("@category:{yellow}")).TotalResults);
            Assert.Equal(0, cl.Search(new Query("@category:{purple}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("@category:{orange\\;purple}")).TotalResults);
            search = cl.Search(new Query("hello"));
            Output.WriteLine("Post-search: " + search.TotalResults);
            foreach (var doc in search.Documents)
            {
                Output.WriteLine("Found: " + doc.Id);
            }
            Assert.Equal(4, search.TotalResults);
        }

        [Fact]
        public void TestGetTagFieldWithNonDefaultSeparator()
        {
            Client cl = GetClient();
            Schema sc = new Schema()
                    .AddTextField("title", 1.0)
                    .AddTagField("category", ";");

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));
            var fields1 = new Dictionary<string, RedisValue>();
            fields1.Add("title", "hello world");
            fields1.Add("category", "red");
            Assert.True(cl.AddDocument("foo", fields1));
            var fields2 = new Dictionary<string, RedisValue>();
            fields2.Add("title", "hello world");
            fields2.Add("category", "blue");
            Assert.True(cl.AddDocument("bar", fields2));
            var fields3 = new Dictionary<string, RedisValue>();
            fields3.Add("title", "hello world");
            fields3.Add("category", "green;yellow");
            Assert.True(cl.AddDocument("baz", fields3));
            var fields4 = new Dictionary<string, RedisValue>();
            fields4.Add("title", "hello world");
            fields4.Add("category", "orange,purple");
            Assert.True(cl.AddDocument("qux", fields4));

            Assert.Equal(1, cl.Search(new Query("@category:{red}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("@category:{blue}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("hello @category:{red}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("hello @category:{blue}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("hello @category:{yellow}")).TotalResults);
            Assert.Equal(0, cl.Search(new Query("@category:{purple}")).TotalResults);
            Assert.Equal(1, cl.Search(new Query("@category:{orange\\,purple}")).TotalResults);
            Assert.Equal(4, cl.Search(new Query("hello")).TotalResults);
        }

        [Fact]
        public void TestMultiDocuments()
        {
            Client cl = GetClient();
            Schema sc = new Schema().AddTextField("title", 1.0).AddTextField("body", 1.0);

            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));

            var fields = new Dictionary<string, RedisValue>();
            fields.Add("title", "hello world");
            fields.Add("body", "lorem ipsum");

            var results = cl.AddDocuments(new Document("doc1", fields), new Document("doc2", fields), new Document("doc3", fields));

            Assert.Equal(new[] { true, true, true }, results);

            Assert.Equal(3, cl.Search(new Query("hello world")).TotalResults);

            results = cl.AddDocuments(new Document("doc4", fields), new Document("doc2", fields), new Document("doc5", fields));
            Assert.Equal(new[] { true, false, true }, results);

            results = cl.DeleteDocuments(true, "doc1", "doc2", "doc36");
            Assert.Equal(new[] { true, true, false }, results);
        }

        [Fact]
        public void TestReturnFields()
        {
            Client cl = GetClient();

            Schema sc = new Schema().AddTextField("field1", 1.0).AddTextField("field2", 1.0);
            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));


            var doc = new Dictionary<string, RedisValue>();
            doc.Add("field1", "value1");
            doc.Add("field2", "value2");
            // Store it
            Assert.True(cl.AddDocument("doc", doc));

            // Query
            SearchResult res = cl.Search(new Query("*").ReturnFields("field1"));
            Assert.Equal(1, res.TotalResults);
            Assert.Equal("value1", res.Documents[0]["field1"]);
            Assert.Null((string)res.Documents[0]["field2"]);
        }

        [Fact]
        public void TestInKeys()
        {
            Client cl = GetClient();
            Schema sc = new Schema().AddTextField("field1", 1.0).AddTextField("field2", 1.0);
            Assert.True(cl.CreateIndex(sc, new ConfiguredIndexOptions()));

            var doc = new Dictionary<string, RedisValue>();
            doc.Add("field1", "value");
            doc.Add("field2", "not");

            // Store it
            Assert.True(cl.AddDocument("doc1", doc));
            Assert.True(cl.AddDocument("doc2", doc));

            // Query
            SearchResult res = cl.Search(new Query("value").LimitKeys("doc1"));
            Assert.Equal(1, res.TotalResults);
            Assert.Equal("doc1", res.Documents[0].Id);
            Assert.Equal("value", res.Documents[0]["field1"]);
            Assert.Null((string)res.Documents[0]["value"]);
        }
    }
}
