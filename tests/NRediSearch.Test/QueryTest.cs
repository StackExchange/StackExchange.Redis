using System.Collections.Generic;
using Xunit;

namespace NRediSearch.Test
{
    public class QueryTest
    {
        public static Query GetQuery() => new Query("hello world");

        [Fact]
        public void GetNoContent()
        {
            var query = GetQuery();
            Assert.False(query.NoContent);
            Assert.Same(query, query.SetNoContent());
            Assert.True(query.NoContent);
        }

        [Fact]
        public void GetWithScores()
        {
            var query = GetQuery();
            Assert.False(query.WithScores);
            Assert.Same(query, query.SetWithScores());
            Assert.True(query.WithScores);
        }

        [Fact]
        public void SerializeRedisArgs()
        {
            var query = new Query("hello world")
            {
                NoContent = true,
                Language = "",
                NoStopwords = true,
                Verbatim = true,
                WithPayloads = true,
                WithScores = true,
                Scoring = "TFIDF.DOCNORM",
                ExplainScore = true
            };

            var args = new List<object>();
            query.SerializeRedisArgs(args);

            Assert.Equal(11, args.Count);
            Assert.Equal(query.QueryString, (string)args[0]);
            Assert.Contains("NOCONTENT".Literal(), args);
            Assert.Contains("NOSTOPWORDS".Literal(), args);
            Assert.Contains("VERBATIM".Literal(), args);
            Assert.Contains("WITHPAYLOADS".Literal(), args);
            Assert.Contains("WITHSCORES".Literal(), args);
            Assert.Contains("LANGUAGE".Literal(), args);
            Assert.Contains("", args);
            Assert.Contains("SCORER".Literal(), args);
            Assert.Contains("TFIDF.DOCNORM", args);
            Assert.Contains("EXPLAINSCORE".Literal(), args);

            var languageIndex = args.IndexOf("LANGUAGE".Literal());
            Assert.Equal("", args[languageIndex + 1]);

            var scoringIndex = args.IndexOf("SCORER".Literal());
            Assert.Equal("TFIDF.DOCNORM", args[scoringIndex + 1]);
        }

        [Fact]
        public void Limit()
        {
            var query = GetQuery();
            Assert.Equal(0, query._paging.Offset);
            Assert.Equal(10, query._paging.Count);
            Assert.Same(query, query.Limit(1, 30));
            Assert.Equal(1, query._paging.Offset);
            Assert.Equal(30, query._paging.Count);
        }

        [Fact]
        public void AddFilter()
        {
            var query = GetQuery();
            Assert.Empty(query._filters);
            Query.NumericFilter f = new Query.NumericFilter("foo", 0, 100);
            Assert.Same(query, query.AddFilter(f));
            Assert.Same(f, query._filters[0]);
        }

        [Fact]
        public void SetVerbatim()
        {
            var query = GetQuery();
            Assert.False(query.Verbatim);
            Assert.Same(query, query.SetVerbatim());
            Assert.True(query.Verbatim);
        }

        [Fact]
        public void SetNoStopwords()
        {
            var query = GetQuery();
            Assert.False(query.NoStopwords);
            Assert.Same(query, query.SetNoStopwords());
            Assert.True(query.NoStopwords);
        }

        [Fact]
        public void SetLanguage()
        {
            var query = GetQuery();
            Assert.Null(query.Language);
            Assert.Same(query, query.SetLanguage("chinese"));
            Assert.Equal("chinese", query.Language);
        }

        [Fact]
        public void LimitFields()
        {
            var query = GetQuery();
            Assert.Null(query._fields);
            Assert.Same(query, query.LimitFields("foo", "bar"));
            Assert.Equal(2, query._fields.Length);
        }

        [Fact]
        public void ReturnFields()
        {
            var query = GetQuery();

            Assert.Null(query._returnFields);
            Assert.Same(query, query.ReturnFields("foo", "bar"));
            Assert.Equal(2, query._returnFields.Length);
        }

        [Fact]
        public void HighlightFields()
        {
            var query = GetQuery();
            Assert.False(query._wantsHighlight);
            Assert.Null(query._highlightFields);

            query = new Query("Hello");
            Assert.Same(query, query.HighlightFields("foo", "bar"));
            Assert.Equal(2, query._highlightFields.Length);
            Assert.Null(query._highlightTags);
            Assert.True(query._wantsHighlight);

            query = new Query("Hello").HighlightFields();
            Assert.Null(query._highlightFields);
            Assert.Null(query._highlightTags);
            Assert.True(query._wantsHighlight);

            Assert.Same(query, query.HighlightFields(new Query.HighlightTags("<b>", "</b>")));
            Assert.Null(query._highlightFields);
            Assert.NotNull(query._highlightTags);
            Assert.Equal("<b>", query._highlightTags.Value.Open);
            Assert.Equal("</b>", query._highlightTags.Value.Close);
        }

        [Fact]
        public void SummarizeFields()
        {
            var query = GetQuery();
            Assert.False(query._wantsSummarize);
            Assert.Null(query._summarizeFields);

            query = new Query("Hello");
            Assert.Equal(query, query.SummarizeFields());
            Assert.True(query._wantsSummarize);
            Assert.Null(query._summarizeFields);
            Assert.Equal(-1, query._summarizeFragmentLen);
            Assert.Equal(-1, query._summarizeNumFragments);

            query = new Query("Hello");
            Assert.Equal(query, query.SummarizeFields("someField"));
            Assert.True(query._wantsSummarize);
            Assert.Single(query._summarizeFields);
            Assert.Equal(-1, query._summarizeFragmentLen);
            Assert.Equal(-1, query._summarizeNumFragments);
        }

        [Fact]
        public void SetScoring()
        {
            var query = GetQuery();
            Assert.Null(query.Scoring);
            Assert.Same(query, query.SetScoring("TFIDF.DOCNORM"));
            Assert.Equal("TFIDF.DOCNORM", query.Scoring);
        }
    }
}
