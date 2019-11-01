using NRediSearch.Aggregation;
using NRediSearch.Aggregation.Reducers;
using Xunit;
using Xunit.Abstractions;

namespace NRediSearch.Test.Aggregation
{
    public class AggregationBuilderTests : RediSearchTestBase
    {
        public AggregationBuilderTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void TestAggregations()
        {
            Client cl = GetClient();
            Schema sc = new Schema();

            sc.AddSortableTextField("name", 1.0);
            sc.AddSortableNumericField("count");
            cl.CreateIndex(sc, Client.IndexOptions.Default);
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
            Assert.Equal("", r1.Value.GetString("nosuchcol"));

            Row? r2 = res.GetRow(1);

            Assert.NotNull(r2);
            Assert.Equal("abc", r2.Value.GetString("name"));
            Assert.Equal(10L, r2.Value.GetInt64("sum"));
        }

        [Fact]
        public void TestApplyAndFilterAggregations()
        {

        }

        [Fact]
        public void TestCursor()
        {

        }
    }
}
