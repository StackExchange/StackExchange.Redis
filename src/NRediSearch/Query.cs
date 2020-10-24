// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;
using System.Globalization;
using StackExchange.Redis;

namespace NRediSearch
{
    /// <summary>
    ///  Query represents query parameters and filters to load results from the engine
    /// </summary>
    public sealed class Query
    {
        /// <summary>
        /// Filter represents a filtering rules in a query 
        /// </summary>
        public abstract class Filter
        {
            public string Property { get; }

            internal abstract void SerializeRedisArgs(List<object> args);

            internal Filter(string property)
            {
                Property = property;
            }
        }

        /// <summary>
        /// NumericFilter wraps a range filter on a numeric field. It can be inclusive or exclusive
        /// </summary>
        public class NumericFilter : Filter
        {
            private readonly double min, max;
            private readonly bool exclusiveMin, exclusiveMax;

            public NumericFilter(string property, double min, bool exclusiveMin, double max, bool exclusiveMax) : base(property)
            {
                this.min = min;
                this.max = max;
                this.exclusiveMax = exclusiveMax;
                this.exclusiveMin = exclusiveMin;
            }

            public NumericFilter(string property, double min, double max) : this(property, min, false, max, false) { }

            internal override void SerializeRedisArgs(List<object> args)
            {
                static RedisValue FormatNum(double num, bool exclude)
                {
                    if (!exclude || double.IsInfinity(num))
                    {
                        return (RedisValue)num; // can use directly
                    }
                    // need to add leading bracket
                    return "(" + num.ToString("G17", NumberFormatInfo.InvariantInfo);
                }
                args.Add("FILTER".Literal());
                args.Add(Property);
                args.Add(FormatNum(min, exclusiveMin));
                args.Add(FormatNum(max, exclusiveMax));
            }
        }

        /// <summary>
        /// GeoFilter encapsulates a radius filter on a geographical indexed fields 
        /// </summary>
        public class GeoFilter : Filter
        {
            private readonly double lon, lat, radius;
            private readonly GeoUnit unit;

            public GeoFilter(string property, double lon, double lat, double radius, GeoUnit unit) : base(property)
            {
                this.lon = lon;
                this.lat = lat;
                this.radius = radius;
                this.unit = unit;
            }

            internal override void SerializeRedisArgs(List<object> args)
            {
                args.Add("GEOFILTER".Literal());
                args.Add(Property);
                args.Add(lon);
                args.Add(lat);
                args.Add(radius);
                args.Add(unit.AsRedisString().Literal());
            }
        }

        internal readonly struct Paging
        {
            public int Offset { get; }
            public int Count { get; }

            public Paging(int offset, int count)
            {
                Offset = offset;
                Count = count;
            }
        }

        /// <summary>
        /// The query's filter list. We only support AND operation on all those filters 
        /// </summary>
        internal readonly List<Filter> _filters = new List<Filter>();

        /// <summary>
        /// The textual part of the query 
        /// </summary>
        public string QueryString { get; }

        /// <summary>
        /// The sorting parameters 
        /// </summary>
        internal Paging _paging = new Paging(0, 10);

        /// <summary>
        /// Set the query to verbatim mode, disabling stemming and query expansion
        /// </summary>
        public bool Verbatim { get; set; }
        /// <summary>
        /// Set the query not to return the contents of documents, and rather just return the ids
        /// </summary>
        public bool NoContent { get; set; }
        /// <summary>
        /// Set the query not to filter for stopwords. In general this should not be used
        /// </summary>
        public bool NoStopwords { get; set; }
        /// <summary>
        /// Set the query to return a factored score for each results. This is useful to merge results from multiple queries.
        /// </summary>
        public bool WithScores { get; set; }
        /// <summary>
        /// Set the query to return object payloads, if any were given
        /// </summary>
        public bool WithPayloads { get; set; }

        /// <summary>
        /// Set the query language, for stemming purposes; see http://redisearch.io for documentation on languages and stemming
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// Set the query scoring. see https://oss.redislabs.com/redisearch/Scoring.html for documentation
        /// </summary>
        public string Scoring { get; set; }
        public bool ExplainScore { get; set; }

        internal string[] _fields = null;
        internal string[] _keys = null;
        internal string[] _returnFields = null;
        /// <summary>
        /// Set the query payload to be evaluated by the scoring function
        /// </summary>
        public byte[] Payload { get; set; }

        /// <summary>
        /// Set the query parameter to sort by
        /// </summary>
        public string SortBy { get; set; }

        /// <summary>
        /// Set the query parameter to sort by ASC by default
        /// </summary>
        public bool SortAscending { get; set; } = true;

        // highlight and summarize
        internal bool _wantsHighlight = false, _wantsSummarize = false;
        internal string[] _highlightFields = null;
        internal string[] _summarizeFields = null;
        internal HighlightTags? _highlightTags = null;
        internal string _summarizeSeparator = null;
        internal int _summarizeNumFragments = -1, _summarizeFragmentLen = -1;

        /// <summary>
        /// Create a new index
        /// </summary>
        /// <param name="queryString">The query string to use for this query.</param>
        public Query(string queryString)
        {
            QueryString = queryString;
        }

        internal void SerializeRedisArgs(List<object> args)
        {
            args.Add(QueryString);

            if (Verbatim)
            {
                args.Add("VERBATIM".Literal());
            }
            if (NoContent)
            {
                args.Add("NOCONTENT".Literal());
            }
            if (NoStopwords)
            {
                args.Add("NOSTOPWORDS".Literal());
            }
            if (WithScores)
            {
                args.Add("WITHSCORES".Literal());
            }
            if (WithPayloads)
            {
                args.Add("WITHPAYLOADS".Literal());
            }
            if (Language != null)
            {
                args.Add("LANGUAGE".Literal());
                args.Add(Language);
            }
            if (_fields?.Length > 0)
            {
                args.Add("INFIELDS".Literal());
                args.Add(_fields.Length.Boxed());
                args.AddRange(_fields);
            }
            if (_keys?.Length > 0)
            {
                args.Add("INKEYS".Literal());
                args.Add(_keys.Length.Boxed());
                args.AddRange(_keys);
            }
            if (_returnFields?.Length > 0)
            {
                args.Add("RETURN".Literal());
                args.Add(_returnFields.Length.Boxed());
                args.AddRange(_returnFields);
            }

            if (SortBy != null)
            {
                args.Add("SORTBY".Literal());
                args.Add(SortBy);
                args.Add((SortAscending ? "ASC" : "DESC").Literal());
            }

            if (Scoring != null)
            {
                args.Add("SCORER".Literal());
                args.Add(Scoring);

                if (ExplainScore)
                {
                    args.Add("EXPLAINSCORE".Literal());
                }
            }

            if (Payload != null)
            {
                args.Add("PAYLOAD".Literal());
                args.Add(Payload);
            }

            if (_paging.Offset != 0 || _paging.Count != 10)
            {
                args.Add("LIMIT".Literal());
                args.Add(_paging.Offset.Boxed());
                args.Add(_paging.Count.Boxed());
            }

            if (_filters?.Count > 0)
            {
                foreach (var f in _filters)
                {
                    f.SerializeRedisArgs(args);
                }
            }

            if (_wantsHighlight)
            {
                args.Add("HIGHLIGHT".Literal());
                if (_highlightFields != null)
                {
                    args.Add("FIELDS".Literal());
                    args.Add(_highlightFields.Length.Boxed());
                    foreach (var s in _highlightFields)
                    {
                        args.Add(s);
                    }
                }
                if (_highlightTags != null)
                {
                    args.Add("TAGS".Literal());
                    var tags = _highlightTags.GetValueOrDefault();
                    args.Add(tags.Open);
                    args.Add(tags.Close);
                }
            }
            if (_wantsSummarize)
            {
                args.Add("SUMMARIZE".Literal());
                if (_summarizeFields != null)
                {
                    args.Add("FIELDS".Literal());
                    args.Add(_summarizeFields.Length.Boxed());
                    foreach (var s in _summarizeFields)
                    {
                        args.Add(s);
                    }
                }
                if (_summarizeNumFragments != -1)
                {
                    args.Add("FRAGS".Literal());
                    args.Add(_summarizeNumFragments.Boxed());
                }
                if (_summarizeFragmentLen != -1)
                {
                    args.Add("LEN".Literal());
                    args.Add(_summarizeFragmentLen.Boxed());
                }
                if (_summarizeSeparator != null)
                {
                    args.Add("SEPARATOR".Literal());
                    args.Add(_summarizeSeparator);
                }
            }

            if (_keys != null && _keys.Length > 0)
            {
                args.Add("INKEYS".Literal());
                args.Add(_keys.Length.Boxed());

                foreach (var key in _keys)
                {
                    args.Add(key);
                }
            }

            if (_returnFields != null && _returnFields.Length > 0)
            {
                args.Add("RETURN".Literal());
                args.Add(_returnFields.Length.Boxed());

                foreach (var returnField in _returnFields)
                {
                    args.Add(returnField);
                }
            }
        }

        /// <summary>
        /// Limit the results to a certain offset and limit
        /// </summary>
        /// <param name="offset">the first result to show, zero based indexing</param>
        /// <param name="count">how many results we want to show</param>
        /// <returns>the query itself, for builder-style syntax</returns>
        public Query Limit(int offset, int count)
        {
            _paging = new Paging(offset, count);
            return this;
        }

        /// <summary>
        /// Add a filter to the query's filter list
        /// </summary>
        /// <param name="f">either a numeric or geo filter object</param>
        /// <returns>the query itself</returns>
        public Query AddFilter(Filter f)
        {
            _filters.Add(f);
            return this;
        }

        /// <summary>
        /// Limit the query to results that are limited to a specific set of fields
        /// </summary>
        /// <param name="fields">a list of TEXT fields in the schemas</param>
        /// <returns>the query object itself</returns>
        public Query LimitFields(params string[] fields)
        {
            _fields = fields;
            return this;
        }

        /// <summary>
        /// Limit the query to results that are limited to a specific set of keys
        /// </summary>
        /// <param name="keys">a list of the TEXT fields in the schemas</param>
        /// <returns>the query object itself</returns>
        public Query LimitKeys(params string[] keys)
        {
            _keys = keys;
            return this;
        }

        /// <summary>
        /// Result's projection - the fields to return by the query
        /// </summary>
        /// <param name="fields">fields a list of TEXT fields in the schemas</param>
        /// <returns>the query object itself</returns>
        public Query ReturnFields(params string[] fields)
        {
            _returnFields = fields;
            return this;
        }

        public readonly struct HighlightTags
        {
            public HighlightTags(string open, string close)
            {
                Open = open;
                Close = close;
            }
            public string Open { get; }
            public string Close { get; }
        }

        public Query HighlightFields(HighlightTags tags, params string[] fields) => HighlightFieldsImpl(tags, fields);
        public Query HighlightFields(params string[] fields) => HighlightFieldsImpl(null, fields);
        private Query HighlightFieldsImpl(HighlightTags? tags, string[] fields)
        {
            if (fields == null || fields.Length > 0)
            {
                _highlightFields = fields;
            }
            _highlightTags = tags;
            _wantsHighlight = true;
            return this;
        }

        public Query SummarizeFields(int contextLen, int fragmentCount, string separator, params string[] fields)
        {
            if (fields == null || fields.Length > 0)
            {
                _summarizeFields = fields;
            }
            _summarizeFragmentLen = contextLen;
            _summarizeNumFragments = fragmentCount;
            _summarizeSeparator = separator;
            _wantsSummarize = true;
            return this;
        }

        public Query SummarizeFields(params string[] fields) => SummarizeFields(-1, -1, null, fields);

        /// <summary>
        /// Set the query to be sorted by a sortable field defined in the schema
        /// </summary>
        /// <param name="field">the sorting field's name</param>
        /// <param name="ascending">if set to true, the sorting order is ascending, else descending</param>
        /// <returns>the query object itself</returns>
        public Query SetSortBy(string field, bool ascending = true)
        {
            SortBy = field;
            SortAscending = ascending;
            return this;
        }

        public Query SetWithScores(bool value = true)
        {
            WithScores = value;
            return this;
        }

        public Query SetNoContent(bool value = true)
        {
            NoContent = value;
            return this;
        }

        public Query SetVerbatim(bool value = true)
        {
            Verbatim = value;
            return this;
        }

        public Query SetNoStopwords(bool value = true)
        {
            NoStopwords = value;
            return this;
        }
        public Query SetLanguage(string language)
        {
            Language = language;
            return this;
        }

        /// <summary>
        /// RediSearch comes with a few very basic scoring functions to evaluate document relevance. They are all based on document scores and term frequency.
        /// This is regardless of the ability to use sortable fields.
        /// Scoring functions are specified by adding the SCORER {scorer_name} argument to a search query.
        /// If you prefer a custom scoring function, it is possible to add more functions using the Extension API.
        /// These are the pre-bundled scoring functions available in RediSearch and how they work.Each function is mentioned by registered name,
        /// that can be passed as a SCORER argument in FT.SEARCH
        /// Pre-bundled scoring:
        /// - TFIDF (default) (https://oss.redislabs.com/redisearch/Scoring.html#tfidf_default)
        /// - TFIDF.DOCNORM (https://oss.redislabs.com/redisearch/Scoring.html#tfidfdocnorm)
        /// - BM25 (https://oss.redislabs.com/redisearch/Scoring.html#bm25)
        /// - DISMAX (https://oss.redislabs.com/redisearch/Scoring.html#dismax)
        /// - DOCSCORE (https://oss.redislabs.com/redisearch/Scoring.html#docscore)
        /// - HAMMING (https://oss.redislabs.com/redisearch/Scoring.html#hamming)
        /// </summary>
        /// <param name="scoring"></param>
        /// <returns></returns>
        public Query SetScoring(string scoring)
        {
            Scoring = scoring;
            return this;
        }
    }
}
