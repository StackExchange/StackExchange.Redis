// .NET port of https://github.com/RedisLabs/JRediSearch/

using NRediSearch.Aggregation;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NRediSearch
{
    public sealed class Client
    {
        [Flags]
        public enum IndexOptions
        {
            /// <summary>
            /// All options disabled
            /// </summary>
            None = 0,
            /// <summary>
            /// Set this to tell the index not to save term offset vectors. This reduces memory consumption but does not
            /// allow performing exact matches, and reduces overall relevance of multi-term queries
            /// </summary>
            UseTermOffsets = 1,
            /// <summary>
            /// If set (default), we keep flags per index record telling us what fields the term appeared on,
            /// and allowing us to filter results by field
            /// </summary>
            KeepFieldFlags = 2,
            /// <summary>
            /// The default indexing options - use term offsets and keep fields flags
            /// </summary>
            Default = UseTermOffsets | KeepFieldFlags,
            /// <summary>
            /// If set, we keep an index of the top entries per term, allowing extremely fast single word queries
            /// regardless of index size, at the cost of more memory
            /// </summary>
            UseScoreIndexes = 4,
            /// <summary>
            /// If set, we will disable the Stop-Words completely
            /// </summary>
            DisableStopWords = 8
        }

        public sealed class ConfiguredIndexOptions
        {
            private IndexOptions _options;
            private string[] _stopwords;
            public ConfiguredIndexOptions(IndexOptions options)
            {
                _options = options;
            }

            /// <summary>
            /// Set a custom stopword list.
            /// </summary>
            /// <param name="stopwords">The new stopwords to use.</param>
            public ConfiguredIndexOptions SetStopwords(params string[] stopwords)
            {
                _stopwords = stopwords ?? throw new ArgumentNullException(nameof(stopwords));
                if (stopwords.Length == 0) _options |= IndexOptions.DisableStopWords;
                else _options &= ~IndexOptions.DisableStopWords;
                return this;
            }

            internal void SerializeRedisArgs(List<object> args)
            {
                SerializeRedisArgs(_options, args);
                if (_stopwords != null && _stopwords.Length != 0)
                {
                    // note that DisableStopWords will not be set in this case
                    args.Add("STOPWORDS".Literal());
                    args.Add(_stopwords.Length.Boxed());
                    foreach (var word in _stopwords)
                        args.Add(word);
                }
            }

            internal static void SerializeRedisArgs(IndexOptions flags, List<object> args)
            {
                if ((flags & IndexOptions.UseTermOffsets) == 0)
                {
                    args.Add("NOOFFSETS".Literal());
                }
                if ((flags & IndexOptions.KeepFieldFlags) == 0)
                {
                    args.Add("NOFIELDS".Literal());
                }
                if ((flags & IndexOptions.UseScoreIndexes) == 0)
                {
                    args.Add("NOSCOREIDX".Literal());
                }
                if ((flags & IndexOptions.DisableStopWords) == IndexOptions.DisableStopWords)
                {
                    args.Add("STOPWORDS".Literal());
                    args.Add(0.Boxed());
                }
            }
        }

        private readonly IDatabaseAsync _db;
        private IDatabase DbSync
            => (_db as IDatabase) ?? throw new InvalidOperationException("Synchronous operations are not available on this database instance");

        private readonly object _boxedIndexName;
        public RedisKey IndexName => (RedisKey)_boxedIndexName;
        public Client(RedisKey indexName, IDatabaseAsync db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _boxedIndexName = indexName; // only box once, not per-command
        }

        public Client(RedisKey indexName, IDatabase db) : this(indexName, (IDatabaseAsync)db) { }

        /// <summary>
        /// Create the index definition in redis
        /// </summary>
        /// <param name="schema">a schema definition <seealso cref="Schema"/></param>
        /// <param name="options">index option flags <seealso cref="IndexOptions"/></param>
        /// <returns>true if successful</returns>
        public bool CreateIndex(Schema schema, IndexOptions options)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            ConfiguredIndexOptions.SerializeRedisArgs(options, args);
            args.Add("SCHEMA".Literal());

            foreach (var f in schema.Fields)
            {
                f.SerializeRedisArgs(args);
            }

            return (string)DbSync.Execute("FT.CREATE", args) == "OK";
        }

        /// <summary>
        /// Create the index definition in redis
        /// </summary>
        /// <param name="schema">a schema definition <seealso cref="Schema"/></param>
        /// <param name="options">index option flags <seealso cref="IndexOptions"/></param>
        /// <returns>true if successful</returns>
        public bool CreateIndex(Schema schema, ConfiguredIndexOptions options)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            options.SerializeRedisArgs(args);
            args.Add("SCHEMA".Literal());

            foreach (var f in schema.Fields)
            {
                f.SerializeRedisArgs(args);
            }

            return (string)DbSync.Execute("FT.CREATE", args) == "OK";
        }

        /// <summary>
        /// Create the index definition in redis
        /// </summary>
        /// <param name="schema">a schema definition <seealso cref="Schema"/></param>
        /// <param name="options">index option flags <seealso cref="IndexOptions"/></param>
        /// <returns>true if successful</returns>
        public async Task<bool> CreateIndexAsync(Schema schema, IndexOptions options)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            ConfiguredIndexOptions.SerializeRedisArgs(options, args);
            args.Add("SCHEMA".Literal());

            foreach (var f in schema.Fields)
            {
                f.SerializeRedisArgs(args);
            }

            return (string)await _db.ExecuteAsync("FT.CREATE", args).ConfigureAwait(false) == "OK";
        }

        /// <summary>
        /// Create the index definition in redis
        /// </summary>
        /// <param name="schema">a schema definition <seealso cref="Schema"/></param>
        /// <param name="options">index option flags <seealso cref="IndexOptions"/></param>
        /// <returns>true if successful</returns>
        public async Task<bool> CreateIndexAsync(Schema schema, ConfiguredIndexOptions options)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            options.SerializeRedisArgs(args);
            args.Add("SCHEMA".Literal());

            foreach (var f in schema.Fields)
            {
                f.SerializeRedisArgs(args);
            }

            return (string)await _db.ExecuteAsync("FT.CREATE", args).ConfigureAwait(false) == "OK";
        }

        /// <summary>
        /// Search the index
        /// </summary>
        /// <param name="q">a <see cref="Query"/> object with the query string and optional parameters</param>
        /// <returns>a <see cref="SearchResult"/> object with the results</returns>
        public SearchResult Search(Query q)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            q.SerializeRedisArgs(args);

            var resp = (RedisResult[])DbSync.Execute("FT.SEARCH", args);
            return new SearchResult(resp, !q.NoContent, q.WithScores, q.WithPayloads);
        }

        /// <summary>
        /// Search the index
        /// </summary>
        /// <param name="q">a <see cref="Query"/> object with the query string and optional parameters</param>
        /// <returns>a <see cref="SearchResult"/> object with the results</returns>
        public async Task<SearchResult> SearchAsync(Query q)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            q.SerializeRedisArgs(args);

            var resp = (RedisResult[])await _db.ExecuteAsync("FT.SEARCH", args).ConfigureAwait(false);
            return new SearchResult(resp, !q.NoContent, q.WithScores, q.WithPayloads);
        }

        /// <summary>
        /// Return Distinct Values in a TAG field
        /// </summary>
        /// <param name="fieldName">TAG field name</param>
        /// <returns>List of TAG field values</returns>
        public RedisValue[] TagVals(string fieldName) =>
            (RedisValue[])DbSync.Execute("FT.TAGVALS", _boxedIndexName, fieldName);

        /// <summary>
        /// Return Distinct Values in a TAG field
        /// </summary>
        /// <param name="fieldName">TAG field name</param>
        /// <returns>List of TAG field values</returns>
        public async Task<RedisValue[]> TagValsAsync(string fieldName) =>
            (RedisValue[])await _db.ExecuteAsync("FT.TAGVALS", _boxedIndexName, fieldName).ConfigureAwait(false);

        /// <summary>
        /// Add a single document to the query
        /// </summary>
        /// <param name="docId">the id of the document. It cannot belong to a document already in the index unless replace is set</param>
        /// <param name="score">the document's score, floating point number between 0 and 1</param>
        /// <param name="fields">a map of the document's fields</param>
        /// <param name="noSave">if set, we only index the document and do not save its contents. This allows fetching just doc ids</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <param name="payload">if set, we can save a payload in the index to be retrieved or evaluated by scoring functions on the server</param>
        public bool AddDocument(string docId, Dictionary<string, RedisValue> fields, double score = 1.0, bool noSave = false, bool replace = false, byte[] payload = null)
        {
            var args = BuildAddDocumentArgs(docId, fields, score, noSave, replace, payload);
            return (string)DbSync.Execute("FT.ADD", args) == "OK";
        }

        /// <summary>
        /// Add a single document to the query
        /// </summary>
        /// <param name="docId">the id of the document. It cannot belong to a document already in the index unless replace is set</param>
        /// <param name="score">the document's score, floating point number between 0 and 1</param>
        /// <param name="fields">a map of the document's fields</param>
        /// <param name="noSave">if set, we only index the document and do not save its contents. This allows fetching just doc ids</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <param name="payload">if set, we can save a payload in the index to be retrieved or evaluated by scoring functions on the server</param>
        public async Task<bool> AddDocumentAsync(string docId, Dictionary<string, RedisValue> fields, double score = 1.0, bool noSave = false, bool replace = false, byte[] payload = null)
        {
            var args = BuildAddDocumentArgs(docId, fields, score, noSave, replace, payload);
            return (string)await _db.ExecuteAsync("FT.ADD", args).ConfigureAwait(false) == "OK";
        }

        /// <summary>
        /// Add a document to the index
        /// </summary>
        /// <param name="doc">The document to add</param>
        /// <param name="options">Options for the operation</param>
        /// <returns>true if the operation succeeded, false otherwise. Note that if the operation fails, an exception will be thrown</returns>
        public bool AddDocument(Document doc, AddOptions options = null)
        {
            var args = BuildAddDocumentArgs(doc.Id, doc._properties, doc.Score, options?.NoSave ?? false, options?.ReplacePolicy ?? AddOptions.ReplacementPolicy.None, doc.Payload, options?.Language);
            return (string)DbSync.Execute("FT.ADD", args) == "OK";
        }

        /// <summary>
        /// Add a document to the index
        /// </summary>
        /// <param name="doc">The document to add</param>
        /// <param name="options">Options for the operation</param>
        /// <returns>true if the operation succeeded, false otherwise. Note that if the operation fails, an exception will be thrown</returns>
        public async Task<bool> AddDocumentAsync(Document doc, AddOptions options = null)
        {
            var args = BuildAddDocumentArgs(doc.Id, doc._properties, doc.Score, options?.NoSave ?? false, options?.ReplacePolicy ?? AddOptions.ReplacementPolicy.None, doc.Payload, options?.Language);
            return (string)await _db.ExecuteAsync("FT.ADD", args).ConfigureAwait(false) == "OK";
        }

        private List<object> BuildAddDocumentArgs(string docId, Dictionary<string, RedisValue> fields, double score, bool noSave, bool replace, byte[] payload)
            => BuildAddDocumentArgs(docId, fields, score, noSave, replace ? AddOptions.ReplacementPolicy.Full : AddOptions.ReplacementPolicy.None, payload, null);
        private List<object> BuildAddDocumentArgs(string docId, Dictionary<string, RedisValue> fields, double score, bool noSave, AddOptions.ReplacementPolicy replacementPolicy, byte[] payload, string language)
        {
            var args = new List<object> { _boxedIndexName, docId, score };
            if (noSave)
            {
                args.Add("NOSAVE".Literal());
            }
            if (replacementPolicy != AddOptions.ReplacementPolicy.None)
            {
                args.Add("REPLACE".Literal());
                if (replacementPolicy == AddOptions.ReplacementPolicy.Partial)
                {
                    args.Add("PARTIAL".Literal());
                }
            }
            if (!string.IsNullOrWhiteSpace(language))
            {
                args.Add("LANGUAGE".Literal());
                args.Add(language);
            }

            if (payload != null)
            {
                args.Add("PAYLOAD".Literal());
                args.Add(payload);
            }

            args.Add("FIELDS".Literal());
            foreach (var ent in fields)
            {
                args.Add(ent.Key);
                args.Add(ent.Value);
            }
            return args;
        }

        /// <summary>
        /// Convenience method for calling AddDocument with replace=true.
        /// </summary>
        /// <param name="docId">The ID of the document to replce.</param>
        /// <param name="fields">The document fields.</param>
        /// <param name="score">The new score.</param>
        /// <param name="payload">The new payload.</param>
        public bool ReplaceDocument(string docId, Dictionary<string, RedisValue> fields, double score = 1.0, byte[] payload = null)
            => AddDocument(docId, fields, score, false, true, payload);

        /// <summary>
        /// Convenience method for calling AddDocumentAsync with replace=true.
        /// </summary>
        /// <param name="docId">The ID of the document to replce.</param>
        /// <param name="fields">The document fields.</param>
        /// <param name="score">The new score.</param>
        /// <param name="payload">The new payload.</param>
        public Task<bool> ReplaceDocumentAsync(string docId, Dictionary<string, RedisValue> fields, double score = 1.0, byte[] payload = null)
            => AddDocumentAsync(docId, fields, score, false, true, payload);

        /// <summary>
        /// Index a document already in redis as a HASH key.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        public bool AddHash(string docId, double score, bool replace) => AddHash((RedisKey)docId, score, replace);

        /// <summary>
        /// Index a document already in redis as a HASH key.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        public bool AddHash(RedisKey docId, double score, bool replace)
        {
            var args = new List<object> { _boxedIndexName, docId, score };
            if (replace)
            {
                args.Add("REPLACE".Literal());
            }
            return (string)DbSync.Execute("FT.ADDHASH", args) == "OK";
        }

        /// <summary>
        /// Index a document already in redis as a HASH key.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        public Task<bool> AddHashAsync(string docId, double score, bool replace) => AddHashAsync((RedisKey)docId, score, replace);

        /// <summary>
        /// Index a document already in redis as a HASH key.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        public async Task<bool> AddHashAsync(RedisKey docId, double score, bool replace)
        {
            var args = new List<object> { _boxedIndexName, docId, score };
            if (replace)
            {
                args.Add("REPLACE".Literal());
            }
            return (string)await _db.ExecuteAsync("FT.ADDHASH", args).ConfigureAwait(false) == "OK";
        }

        /// <summary>
        /// Get the index info, including memory consumption and other statistics.
        /// </summary>
        /// <remarks>TODO: Make a class for easier access to the index properties</remarks>
        /// <returns>a map of key/value pairs</returns>
        public Dictionary<string, RedisValue> GetInfo() =>
            ParseGetInfo(DbSync.Execute("FT.INFO", _boxedIndexName));

        /// <summary>
        /// Get the index info, including memory consumption and other statistics.
        /// </summary>
        /// <remarks>TODO: Make a class for easier access to the index properties</remarks>
        /// <returns>a map of key/value pairs</returns>
        public async Task<Dictionary<string, RedisValue>> GetInfoAsync() =>
            ParseGetInfo(await _db.ExecuteAsync("FT.INFO", _boxedIndexName).ConfigureAwait(false));

        private static Dictionary<string, RedisValue> ParseGetInfo(RedisResult value)
        {
            var res = (RedisResult[])value;
            var info = new Dictionary<string, RedisValue>();
            for (int i = 0; i < res.Length; i += 2)
            {
                var val = res[i + 1];
                if (val.Type != ResultType.MultiBulk)
                {
                    info.Add((string)res[i], (RedisValue)val);
                }
            }
            return info;
        }

        /// <summary>
        /// Delete a document from the index.
        /// </summary>
        /// <param name="docId">the document's id</param>
        /// <returns>true if it has been deleted, false if it did not exist</returns>
        public bool DeleteDocument(string docId)
        {
            return (long)DbSync.Execute("FT.DEL", _boxedIndexName, docId) == 1;
        }

        /// <summary>
        /// Delete a document from the index.
        /// </summary>
        /// <param name="docId">the document's id</param>
        /// <returns>true if it has been deleted, false if it did not exist</returns>
        public async Task<bool> DeleteDocumentAsync(string docId)
        {
            return (long)await _db.ExecuteAsync("FT.DEL", _boxedIndexName, docId).ConfigureAwait(false) == 1;
        }

        /// <summary>
        /// Drop the index and all associated keys, including documents
        /// </summary>
        /// <returns>true on success</returns>
        public bool DropIndex()
        {
            return (string)DbSync.Execute("FT.DROP", _boxedIndexName) == "OK";
        }
        /// <summary>
        /// Drop the index and all associated keys, including documents
        /// </summary>
        /// <returns>true on success</returns>
        public async Task<bool> DropIndexAsync()
        {
            return (string)await _db.ExecuteAsync("FT.DROP", _boxedIndexName).ConfigureAwait(false) == "OK";
        }

        /// <summary>
        /// Optimize memory consumption of the index by removing extra saved capacity. This does not affect speed
        /// </summary>
        public long OptimizeIndex()
        {
            return (long)DbSync.Execute("FT.OPTIMIZE", _boxedIndexName);
        }

        /// <summary>
        /// Optimize memory consumption of the index by removing extra saved capacity. This does not affect speed
        /// </summary>
        public async Task<long> OptimizeIndexAsync()
        {
            return (long)await _db.ExecuteAsync("FT.OPTIMIZE", _boxedIndexName).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the size of an autoc-complete suggestion dictionary
        /// </summary>
        public long CountSuggestions()
            => (long)DbSync.Execute("FT.SUGLEN", _boxedIndexName);

        /// <summary>
        /// Get the size of an autoc-complete suggestion dictionary
        /// </summary>
        public async Task<long> CountSuggestionsAsync()
            => (long)await _db.ExecuteAsync("FT.SUGLEN", _boxedIndexName).ConfigureAwait(false);

        /// <summary>
        /// Add a suggestion string to an auto-complete suggestion dictionary. This is disconnected from the index definitions, and leaves creating and updating suggestino dictionaries to the user.
        /// </summary>
        /// <param name="value">the suggestion string we index</param>
        /// <param name="score">a floating point number of the suggestion string's weight</param>
        /// <param name="increment">if set, we increment the existing entry of the suggestion by the given score, instead of replacing the score. This is useful for updating the dictionary based on user queries in real time</param>
        /// <returns>the current size of the suggestion dictionary.</returns>
        public long AddSuggestion(string value, double score, bool increment = false)
        {
            object[] args = increment
                ? new object[] { _boxedIndexName, value, score, "INCR".Literal() }
                : new object[] { _boxedIndexName, value, score };
            return (long)DbSync.Execute("FT.SUGADD", args);
        }

        /// <summary>
        /// Add a suggestion string to an auto-complete suggestion dictionary. This is disconnected from the index definitions, and leaves creating and updating suggestino dictionaries to the user.
        /// </summary>
        /// <param name="value">the suggestion string we index</param>
        /// <param name="score">a floating point number of the suggestion string's weight</param>
        /// <param name="increment">if set, we increment the existing entry of the suggestion by the given score, instead of replacing the score. This is useful for updating the dictionary based on user queries in real time</param>
        /// <returns>the current size of the suggestion dictionary.</returns>
        public async Task<long> AddSuggestionAsync(string value, double score, bool increment = false)
        {
            object[] args = increment
                ? new object[] { _boxedIndexName, value, score, "INCR".Literal() }
                : new object[] { _boxedIndexName, value, score };
            return (long)await _db.ExecuteAsync("FT.SUGADD", args).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a string from a suggestion index.
        /// </summary>
        /// <param name="value">the string to delete</param>
        public bool DeleteSuggestion(string value)
            => (long)DbSync.Execute("FT.SUGDEL", _boxedIndexName, value) == 1;

        /// <summary>
        /// Delete a string from a suggestion index.
        /// </summary>
        /// <param name="value">the string to delete</param>
        public async Task<bool> DeleteSuggestionAsync(string value)
            => (long)await _db.ExecuteAsync("FT.SUGDEL", _boxedIndexName, value).ConfigureAwait(false) == 1;

        /// <summary>
        /// Get completion suggestions for a prefix
        /// </summary>
        /// <param name="prefix">the prefix to complete on</param>
        /// <param name="fuzzy"> if set,we do a fuzzy prefix search, including prefixes at levenshtein distance of 1 from the prefix sent</param>
        /// <param name="max">If set, we limit the results to a maximum of num. (Note: The default is 5, and the number cannot be greater than 10).</param>
        /// <returns>a list of the top suggestions matching the prefix</returns>
        public string[] GetSuggestions(string prefix, bool fuzzy = false, int max = 5)
        {
            var args = new List<object> { _boxedIndexName, prefix };
            if (fuzzy) args.Add("FUZZY".Literal());
            if (max != 5)
            {
                args.Add("MAX".Literal());
                args.Add(max.Boxed());
            }
            return (string[])DbSync.Execute("FT.SUGGET", args);
        }
        /// <summary>
        /// Get completion suggestions for a prefix
        /// </summary>
        /// <param name="prefix">the prefix to complete on</param>
        /// <param name="fuzzy"> if set,we do a fuzzy prefix search, including prefixes at levenshtein distance of 1 from the prefix sent</param>
        /// <param name="max">If set, we limit the results to a maximum of num. (Note: The default is 5, and the number cannot be greater than 10).</param>
        /// <returns>a list of the top suggestions matching the prefix</returns>
        public async Task<string[]> GetSuggestionsAsync(string prefix, bool fuzzy = false, int max = 5)
        {
            var args = new List<object> { _boxedIndexName, prefix };
            if (fuzzy) args.Add("FUZZY".Literal());
            if (max != 5)
            {
                args.Add("MAX".Literal());
                args.Add(max.Boxed());
            }
            return (string[])await _db.ExecuteAsync("FT.SUGGET", args).ConfigureAwait(false);
        }

        /// <summary>
        /// Perform an aggregate query
        /// </summary>
        /// <param name="query">The query to watch</param>
        public AggregationResult Aggregate(AggregationRequest query)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            query.SerializeRedisArgs(args);

            var resp = DbSync.Execute("FT.AGGREGATE", args);

            return new AggregationResult(resp);
        }
        /// <summary>
        /// Perform an aggregate query
        /// </summary>
        /// <param name="query">The query to watch</param>
        public async Task<AggregationResult> AggregateAsync(AggregationRequest query)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            query.SerializeRedisArgs(args);

            var resp = await _db.ExecuteAsync("FT.AGGREGATE", args).ConfigureAwait(false);

            return new AggregationResult(resp);
        }

        /// <summary>
        /// Generate an explanatory textual query tree for this query string
        /// </summary>
        /// <param name="q">The query to explain</param>
        /// <returns>A string describing this query</returns>
        public string Explain(Query q)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            q.SerializeRedisArgs(args);
            return (string)DbSync.Execute("FT.EXPLAIN", args);
        }

        /// <summary>
        /// Generate an explanatory textual query tree for this query string
        /// </summary>
        /// <param name="q">The query to explain</param>
        /// <returns>A string describing this query</returns>
        public async Task<string> ExplainAsync(Query q)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };
            q.SerializeRedisArgs(args);
            return (string)await _db.ExecuteAsync("FT.EXPLAIN", args).ConfigureAwait(false);
        }

        /// <summary>
        /// Get a document from the index.
        /// </summary>
        /// <param name="docId">The document ID to retrieve.</param>
        /// <returns>The document as stored in the index. If the document does not exist, null is returned.</returns>
        public Document GetDocument(string docId)
            => Document.Parse(docId, DbSync.Execute("FT.GET", _boxedIndexName, docId));

        /// <summary>
        /// Get a document from the index.
        /// </summary>
        /// <param name="docId">The document ID to retrieve.</param>
        /// <returns>The document as stored in the index. If the document does not exist, null is returned.</returns>
        public async Task<Document> GetDocumentAsync(string docId)
            => Document.Parse(docId, await _db.ExecuteAsync("FT.GET", _boxedIndexName, docId).ConfigureAwait(false));

        /// <summary>
        /// Replace specific fields in a document. Unlike #replaceDocument(), fields not present in the field list
        /// are not erased, but retained. This avoids reindexing the entire document if the new values are not
        /// indexed (though a reindex will happen).
        /// </summary>
        /// <param name="docId">The ID of the document.</param>
        /// <param name="fields">The fields and values to update.</param>
        /// <param name="score">The new score of the document.</param>
        public bool UpdateDocument(string docId, Dictionary<string, RedisValue> fields, double score = 1.0)
        {
            var args = BuildAddDocumentArgs(docId, fields, score, false, AddOptions.ReplacementPolicy.Partial, null, null);
            return (string)DbSync.Execute("FT.ADD", args) == "OK";
        }

        /// <summary>
        /// Replace specific fields in a document. Unlike #replaceDocument(), fields not present in the field list
        /// are not erased, but retained. This avoids reindexing the entire document if the new values are not
        /// indexed (though a reindex will happen
        /// </summary>
        /// <param name="docId">The ID of the document.</param>
        /// <param name="fields">The fields and values to update.</param>
        /// <param name="score">The new score of the document.</param>
        public async Task<bool> UpdateDocumentAsync(string docId, Dictionary<string, RedisValue> fields, double score = 1.0)
        {
            var args = BuildAddDocumentArgs(docId, fields, score, false, AddOptions.ReplacementPolicy.Partial, null, null);
            return  (string)await _db.ExecuteAsync("FT.ADD", args).ConfigureAwait(false) == "OK";
        }
    }
}
