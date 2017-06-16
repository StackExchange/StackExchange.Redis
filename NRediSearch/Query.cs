// .NET port of https://github.com/RedisLabs/JRediSearch/

using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NRediSearch
{
    /// <summary>
    ///  Query represents query parameters and filters to load results from the engine
    /// </summary>
    public class Query
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
                RedisValue FormatNum(double num, bool exclude)
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
            private GeoUnit unit;

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

                switch (unit)
                {
                    case GeoUnit.Feet: args.Add("ft".Literal()); break;
                    case GeoUnit.Kilometers: args.Add("km".Literal()); break;
                    case GeoUnit.Meters: args.Add("m".Literal()); break;
                    case GeoUnit.Miles: args.Add("mi".Literal()); break;
                    default: throw new InvalidOperationException($"Unknown unit: {unit}");
                }
            }
        }

        private struct Paging
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
        List<Filter> _filters = new List<Filter>();

        /// <summary>
        /// The textual part of the query 
        /// </summary>
        public string QueryString { get; }
        
        /// <summary>
        /// The sorting parameters 
        /// </summary>
        Paging _paging = new Paging(0, 10);

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
        protected String[] _fields = null;
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
        public bool SortAscending {get; set;} = true;

        /// <summary>
        /// Create a new index
        /// </summary>
        public Query(String queryString)
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
            if (_fields != null && _fields.Length > 0)
            {
                args.Add("INFIELDS".Literal());
                args.Add(_fields.Length);
                args.AddRange(_fields);
            }

            if(SortBy != null)
            {
                args.Add("SORTBY".Literal());
                args.Add(SortBy);
                args.Add((SortAscending ? "ASC" : "DESC").Literal());
            }

            if (Payload != null)
            {
                args.Add("PAYLOAD".Literal());
                args.Add(Payload);
            }

            if (_paging.Offset != 0 || _paging.Count != 10)
            {
                args.Add("LIMIT".Literal());
                args.Add(_paging.Offset);
                args.Add(_paging.Count);
            }

            if (_filters != null && _filters.Count > 0)
            {
                foreach (var f in _filters)
                {
                    f.SerializeRedisArgs(args);
                }
            }
        }
        
        /// <summary>
        /// Limit the results to a certain offset and limit
        /// </summary>
        /// <param name="offset">the first result to show, zero based indexing</param>
        /// <param name="limit">how many results we want to show</param>
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
            this._fields = fields;
            return this;
        }

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
    }
}
