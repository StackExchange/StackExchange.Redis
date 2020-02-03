using System;
using System.Collections.Generic;
using NRediSearch.Aggregation;
using NRediSearch.QueryBuilder;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;
using static NRediSearch.Aggregation.Reducers.Reducers;
using static NRediSearch.Aggregation.SortedField;
using static NRediSearch.QueryBuilder.QueryBuilder;
using static NRediSearch.QueryBuilder.Values;

namespace NRediSearch.Test.QueryBuilder
{
    public class BuilderTest : RediSearchTestBase
    {
        public BuilderTest(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestTag()
        {
            Value v = Tags("foo");
            Assert.Equal("{foo}", v.ToString());
            v = Tags("foo", "bar");
            Assert.Equal("{foo | bar}", v.ToString());
        }

        [Fact]
        public void TestEmptyTag()
        {
            Assert.Throws<ArgumentException>(() => Tags());
        }

        [Fact]
        public void TestRange()
        {
            Value v = Between(1, 10);
            Assert.Equal("[1.0 10.0]", v.ToString());
            v = Between(1, 10).InclusiveMax(false);
            Assert.Equal("[1.0 (10.0]", v.ToString());
            v = Between(1, 10).InclusiveMin(false);
            Assert.Equal("[(1.0 10.0]", v.ToString());

            // le, gt, etc.
            Assert.Equal("[42.0 42.0]", Equal(42).ToString());
            Assert.Equal("[-inf (42.0]", LessThan(42).ToString());
            Assert.Equal("[-inf 42.0]", LessThanOrEqual(42).ToString());
            Assert.Equal("[(42.0 inf]", GreaterThan(42).ToString());
            Assert.Equal("[42.0 inf]", GreaterThanOrEqual(42).ToString());

            // string value
            Assert.Equal("s", Value("s").ToString());

            // Geo value
            Assert.Equal("[1.0 2.0 3.0 km]",
                    new GeoValue(1.0, 2.0, 3.0, GeoUnit.Kilometers).ToString());
        }

        [Fact]
        public void TestIntersectionBasic()
        {
            INode n = Intersect().Add("name", "mark");
            Assert.Equal("@name:mark", n.ToString());

            n = Intersect().Add("name", "mark", "dvir");
            Assert.Equal("@name:(mark dvir)", n.ToString());
        }

        [Fact]
        public void TestIntersectionNested()
        {
            INode n = Intersect().
                    Add(Union("name", Value("mark"), Value("dvir"))).
                    Add("time", Between(100, 200)).
                    Add(Disjunct("created", LessThan(1000)));
            Assert.Equal("(@name:(mark|dvir) @time:[100.0 200.0] -@created:[-inf (1000.0])", n.ToString());
        }

        private static string GetArgsString(AggregationRequest request)
        {
            var args = new List<object>();
            request.SerializeRedisArgs(args);
            return string.Join(" ", args);
        }

        [Fact]
        public void TestAggregation()
        {
            Assert.Equal("*", GetArgsString(new AggregationRequest()));
            AggregationRequest r = new AggregationRequest().
                    GroupBy("@actor", Count().As("cnt")).
                SortBy(Descending("@cnt"));
            Assert.Equal("* GROUPBY 1 @actor REDUCE COUNT 0 AS cnt SORTBY 2 @cnt DESC", GetArgsString(r));

            r = new AggregationRequest().GroupBy("@brand",
                    Quantile("@price", 0.50).As("q50"),
                Quantile("@price", 0.90).As("q90"),
                Quantile("@price", 0.95).As("q95"),
                Avg("@price"),
                Count().As("count")).
                SortByDescending("@count").
                Limit(10);
            Assert.Equal("* GROUPBY 1 @brand REDUCE QUANTILE 2 @price 0.5 AS q50 REDUCE QUANTILE 2 @price 0.9 AS q90 REDUCE QUANTILE 2 @price 0.95 AS q95 REDUCE AVG 1 @price REDUCE COUNT 0 AS count LIMIT 0 10 SORTBY 2 @count DESC",
                    GetArgsString(r));
        }

        [Fact]
        public void TestAggregationBuilder()
        {
            Assert.Equal("*", new AggregationBuilder().GetArgsString());

            AggregationBuilder r1 = new AggregationBuilder()
                .GroupBy("@actor", Count().As("cnt"))
                .SortBy(Descending("@cnt"));

            Assert.Equal("* GROUPBY 1 @actor REDUCE COUNT 0 AS cnt SORTBY 2 @cnt DESC", r1.GetArgsString());

            Group group = new Group("@brand")
                .Reduce(Quantile("@price", 0.50).As("q50"))
                .Reduce(Quantile("@price", 0.90).As("q90"))
                .Reduce(Quantile("@price", 0.95).As("q95"))
                .Reduce(Avg("@price"))
                .Reduce(Count().As("count"));
            AggregationBuilder r2 = new AggregationBuilder()
                .GroupBy(group)
                .Limit(10)
                .SortByDescending("@count");

            Assert.Equal("* GROUPBY 1 @brand REDUCE QUANTILE 2 @price 0.5 AS q50 REDUCE QUANTILE 2 @price 0.9 AS q90 REDUCE QUANTILE 2 @price 0.95 AS q95 REDUCE AVG 1 @price REDUCE COUNT 0 AS count LIMIT 0 10 SORTBY 2 @count DESC",
                    r2.GetArgsString());

            AggregationBuilder r3 = new AggregationBuilder()
                .Load("@count")
                .Apply("@count%1000", "thousands")
                .SortBy(Descending("@count"))
                .Limit(0, 2);
            Assert.Equal("* LOAD 1 @count APPLY @count%1000 AS thousands SORTBY 2 @count DESC LIMIT 0 2", r3.GetArgsString());
        }
    }
}
