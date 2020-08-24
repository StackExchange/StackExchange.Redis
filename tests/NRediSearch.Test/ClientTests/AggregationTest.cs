using System;
using NRediSearch.Aggregation;
using NRediSearch.Aggregation.Reducers;
using Xunit;
using Xunit.Abstractions;
using static NRediSearch.Client;

namespace NRediSearch.Test.ClientTests
{
    public class AggregationTest : RediSearchTestBase
    {
        public AggregationTest(ITestOutputHelper output) : base(output) { }

        [Fact]
        [Obsolete]
        public void TestAggregations()
        {
            /*
             127.0.0.1:6379> FT.CREATE test_index SCHEMA name TEXT SORTABLE count NUMERIC SORTABLE
             OK
             127.0.0.1:6379> FT.ADD test_index data1 1.0 FIELDS name abc count 10
             OK
             127.0.0.1:6379> FT.ADD test_index data2 1.0 FIELDS name def count 5
             OK
             127.0.0.1:6379> FT.ADD test_index data3 1.0 FIELDS name def count 25
             */

            Client cl = GetClient();
            Schema sc = new Schema();
            sc.AddSortableTextField("name", 1.0);
            sc.AddSortableNumericField("count");
            cl.CreateIndex(sc, new ConfiguredIndexOptions());
            cl.AddDocument(new Document("data1").Set("name", "abc").Set("count", 10));
            cl.AddDocument(new Document("data2").Set("name", "def").Set("count", 5));
            cl.AddDocument(new Document("data3").Set("name", "def").Set("count", 25));

            AggregationRequest r = new AggregationRequest()
                    .GroupBy("@name", Reducers.Sum("@count").As("sum"))
                .SortBy(SortedField.Descending("@sum"), 10);

            // actual search
            AggregationResult res = cl.Aggregate(r);
            var r1 = res.GetRow(0);
            Assert.NotNull(r1);
            Assert.Equal("def", r1.Value.GetString("name"));
            Assert.Equal(30, r1.Value.GetInt64("sum"));

            var r2 = res.GetRow(1);
            Assert.NotNull(r2);
            Assert.Equal("abc", r2.Value.GetString("name"));
            Assert.Equal(10, r2.Value.GetInt64("sum"));
        }
    }
}
