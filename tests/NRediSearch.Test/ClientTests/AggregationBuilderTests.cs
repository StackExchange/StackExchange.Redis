using System.Threading;
using NRediSearch.Aggregation;
using NRediSearch.Aggregation.Reducers;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;
using static NRediSearch.Client;

namespace NRediSearch.Test.ClientTests
{
    public class AggregationBuilderTests : RediSearchTestBase
    {
        public AggregationBuilderTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
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

            AggregationBuilder r = new AggregationBuilder()
                .GroupBy("@name", Reducers.Sum("@count").As("sum"))
                .SortBy(10, SortedField.Descending("@sum"));

            // actual search
            AggregationResult res = cl.Aggregate(r);
            Row? r1 = res.GetRow(0);
            Assert.NotNull(r1);
            Assert.Equal("def", r1.Value.GetString("name"));
            Assert.Equal(30, r1.Value.GetInt64("sum"));
            Assert.Equal(30.0, r1.Value.GetDouble("sum"));

            Assert.Equal(0L, r1.Value.GetInt64("nosuchcol"));
            Assert.Equal(0.0, r1.Value.GetDouble("nosuchcol"));
            Assert.Null(r1.Value.GetString("nosuchcol"));

            Row? r2 = res.GetRow(1);

            Assert.NotNull(r2);
            Assert.Equal("abc", r2.Value.GetString("name"));
            Assert.Equal(10L, r2.Value.GetInt64("sum"));
        }

        [Fact]
        public void TestApplyAndFilterAggregations()
        {
            /*
                 127.0.0.1:6379> FT.CREATE test_index SCHEMA name TEXT SORTABLE subj1 NUMERIC SORTABLE subj2 NUMERIC SORTABLE
                 OK
                 127.0.0.1:6379> FT.ADD test_index data1 1.0 FIELDS name abc subj1 20 subj2 70
                 OK
                 127.0.0.1:6379> FT.ADD test_index data2 1.0 FIELDS name def subj1 60 subj2 40
                 OK
                 127.0.0.1:6379> FT.ADD test_index data3 1.0 FIELDS name ghi subj1 50 subj2 80
                 OK
                 127.0.0.1:6379> FT.ADD test_index data1 1.0 FIELDS name abc subj1 30 subj2 20
                 OK
                 127.0.0.1:6379> FT.ADD test_index data2 1.0 FIELDS name def subj1 65 subj2 45
                 OK
                 127.0.0.1:6379> FT.ADD test_index data3 1.0 FIELDS name ghi subj1 70 subj2 70
                 OK
             */

            Client cl = GetClient();
            Schema sc = new Schema();

            sc.AddSortableTextField("name", 1.0);
            sc.AddSortableNumericField("subj1");
            sc.AddSortableNumericField("subj2");
            cl.CreateIndex(sc, new ConfiguredIndexOptions());
            cl.AddDocument(new Document("data1").Set("name", "abc").Set("subj1", 20).Set("subj2", 70));
            cl.AddDocument(new Document("data2").Set("name", "def").Set("subj1", 60).Set("subj2", 40));
            cl.AddDocument(new Document("data3").Set("name", "ghi").Set("subj1", 50).Set("subj2", 80));
            cl.AddDocument(new Document("data4").Set("name", "abc").Set("subj1", 30).Set("subj2", 20));
            cl.AddDocument(new Document("data5").Set("name", "def").Set("subj1", 65).Set("subj2", 45));
            cl.AddDocument(new Document("data6").Set("name", "ghi").Set("subj1", 70).Set("subj2", 70));

            AggregationBuilder r = new AggregationBuilder().Apply("(@subj1+@subj2)/2", "attemptavg")
                .GroupBy("@name", Reducers.Avg("@attemptavg").As("avgscore"))
                .Filter("@avgscore>=50")
                .SortBy(10, SortedField.Ascending("@name"));

            // actual search
            AggregationResult res = cl.Aggregate(r);
            Row? r1 = res.GetRow(0);
            Assert.NotNull(r1);
            Assert.Equal("def", r1.Value.GetString("name"));
            Assert.Equal(52.5, r1.Value.GetDouble("avgscore"));

            Row? r2 = res.GetRow(1);
            Assert.NotNull(r2);
            Assert.Equal("ghi", r2.Value.GetString("name"));
            Assert.Equal(67.5, r2.Value.GetDouble("avgscore"));
        }

        [Fact]
        public void TestCursor()
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

            AggregationBuilder r = new AggregationBuilder()
                .GroupBy("@name", Reducers.Sum("@count").As("sum"))
                .SortBy(10, SortedField.Descending("@sum"))
                .Cursor(1, 3000);

            // actual search
            AggregationResult res = cl.Aggregate(r);
            Row? row = res.GetRow(0);
            Assert.NotNull(row);
            Assert.Equal("def", row.Value.GetString("name"));
            Assert.Equal(30, row.Value.GetInt64("sum"));
            Assert.Equal(30.0, row.Value.GetDouble("sum"));

            Assert.Equal(0L, row.Value.GetInt64("nosuchcol"));
            Assert.Equal(0.0, row.Value.GetDouble("nosuchcol"));
            Assert.Null(row.Value.GetString("nosuchcol"));

            res = cl.CursorRead(res.CursorId, 1);
            Row? row2 = res.GetRow(0);

            Assert.NotNull(row2);
            Assert.Equal("abc", row2.Value.GetString("name"));
            Assert.Equal(10, row2.Value.GetInt64("sum"));

            Assert.True(cl.CursorDelete(res.CursorId));

            try
            {
                cl.CursorRead(res.CursorId, 1);
                Assert.True(false);
            }
            catch (RedisException) { }

            AggregationBuilder r2 = new AggregationBuilder()
                .GroupBy("@name", Reducers.Sum("@count").As("sum"))
                .SortBy(10, SortedField.Descending("@sum"))
                .Cursor(1, 1000);

            Thread.Sleep(1000);

            try
            {
                cl.CursorRead(res.CursorId, 1);
                Assert.True(false);
            }
            catch (RedisException) { }
        }
    }
}
