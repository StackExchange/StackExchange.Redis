// .NET port of https://github.com/RedisLabs/JRediSearch/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NRediSearch.Aggregation;
using StackExchange.Redis;
using static NRediSearch.Schema;
using static NRediSearch.SuggestionOptions;

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
            /// The default indexing options - use term offsets, keep fields flags, keep term frequencies
            /// </summary>
            Default = UseTermOffsets | KeepFieldFlags | KeepTermFrequencies,
            /// <summary>
            /// If set, we keep an index of the top entries per term, allowing extremely fast single word queries
            /// regardless of index size, at the cost of more memory
            /// </summary>
            [Obsolete("'NOSCOREIDX' was removed from RediSearch.", true)]
            UseScoreIndexes = 4,
            /// <summary>
            /// If set, we will disable the Stop-Words completely
            /// </summary>
            DisableStopWords = 8,
            /// <summary>
            /// If set, we keep an index of the top entries per term, allowing extremely fast single word queries
            /// regardless of index size, at the cost of more memory
            /// </summary>
            KeepTermFrequencies = 16
        }

        public sealed class IndexDefinition
        {
            public enum IndexType
            {
                /// <summary>
                /// Used to indicates that the index should follow the keys of type Hash changes
                /// </summary>
                Hash
            }

            internal readonly IndexType _type = IndexType.Hash;
            internal readonly bool _async; 
            internal readonly string[] _prefixes;
            internal readonly string _filter;
            internal readonly string _languageField;
            internal readonly string _language;
            internal readonly string _scoreFiled;
            internal readonly double _score;
            internal readonly string _payloadField;

            public IndexDefinition(bool async = false, string[] prefixes = null,
            string filter = null, string languageField = null, string language = null, 
            string scoreFiled = null, double score = 1.0, string payloadField = null)
            {
                _async = async;
                _prefixes = prefixes;
                _filter = filter;
                _languageField = languageField;
                _language = language;
                _scoreFiled = scoreFiled;
                _score = score;
                _payloadField = payloadField;
            }

            internal void SerializeRedisArgs(List<object> args)
            {
                args.Add("ON".Literal());
                args.Add(_type.ToString("g"));
                if (_async)
                {
                    args.Add("ASYNC".Literal());
                }
                if (_prefixes?.Length > 0) 
                {
                    args.Add("PREFIX".Literal());
                    args.Add(_prefixes.Length.ToString());
                    args.AddRange(_prefixes);
                }
                if (_filter != null) 
                {
                    args.Add("FILTER".Literal());
                    args.Add(_filter);
                }                
                if (_languageField != null) {
                    args.Add("LANGUAGE_FIELD".Literal());
                    args.Add(_languageField);      
                }                
                if (_language != null) {
                    args.Add("LANGUAGE".Literal());
                    args.Add(_language);      
                }                
                if (_scoreFiled != null) {
                    args.Add("SCORE_FIELD".Literal());
                    args.Add(_scoreFiled);      
                }                
                if (_score != 1.0) {
                    args.Add("SCORE".Literal());
                    args.Add(_score.ToString());      
                }
                if (_payloadField != null) {
                    args.Add("PAYLOAD_FIELD".Literal());
                    args.Add(_payloadField);      
                }
            }

        }

        public sealed class ConfiguredIndexOptions
        {
            // This news up a enum which results in the 0 equivalent.
            // It's not used in the library and I'm guessing this isn't intentional.
            public static IndexOptions Default => new IndexOptions();

            private IndexOptions _options;
            private IndexDefinition _definition;
            private string[] _stopwords;

            public ConfiguredIndexOptions(IndexOptions options = IndexOptions.Default)
            {
                _options = options;
            }

            public ConfiguredIndexOptions(IndexDefinition definition, IndexOptions options = IndexOptions.Default) 
            : this(options)
            {
                _definition = definition;
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

            public ConfiguredIndexOptions SetNoStopwords()
            {
                _options |= IndexOptions.DisableStopWords;

                return this;
            }

            internal void SerializeRedisArgs(List<object> args)
            {
                SerializeRedisArgs(_options, args, _definition);
                if (_stopwords?.Length > 0)
                {
                    args.Add("STOPWORDS".Literal());
                    args.Add(_stopwords.Length.Boxed());
                    args.AddRange(_stopwords);
                }
            }

            internal static void SerializeRedisArgs(IndexOptions options, List<object> args, IndexDefinition definition)
            {
                definition?.SerializeRedisArgs(args);
                if ((options & IndexOptions.UseTermOffsets) == 0)
                {
                    args.Add("NOOFFSETS".Literal());
                }
                if ((options & IndexOptions.KeepFieldFlags) == 0)
                {
                    args.Add("NOFIELDS".Literal());
                }
                if ((options & IndexOptions.KeepTermFrequencies) == 0)
                {
                    args.Add("NOFREQS".Literal());
                }
                if ((options & IndexOptions.DisableStopWords) == IndexOptions.DisableStopWords)
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
        /// Alter index add fields
        /// </summary>
        /// <param name="fields">list of fields</param>
        /// <returns>`true` is successful</returns>
        public bool AlterIndex(params Field[] fields)
        {
            var args = new List<object>
            {
                _boxedIndexName,
                "SCHEMA".Literal(),
                "ADD".Literal()
            };

            foreach (var field in fields)
            {
                field.SerializeRedisArgs(args);
            }

            return (string)DbSync.Execute("FT.ALTER", args) == "OK";
        }

        /// <summary>
        /// Alter index add fields
        /// </summary>
        /// <param name="fields">list of fields</param>
        /// <returns>`true` is successful</returns>
        public async Task<bool> AlterIndexAsync(params Field[] fields)
        {
            var args = new List<object>
            {
                _boxedIndexName,
                "SCHEMA".Literal(),
                "ADD".Literal()
            };

            foreach (var field in fields)
            {
                field.SerializeRedisArgs(args);
            }

            return (string)(await _db.ExecuteAsync("FT.ALTER", args).ConfigureAwait(false)) == "OK";
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
            return new SearchResult(resp, !q.NoContent, q.WithScores, q.WithPayloads, q.ExplainScore);
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
            return new SearchResult(resp, !q.NoContent, q.WithScores, q.WithPayloads, q.ExplainScore);
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
        /// <returns>true if the operation succeeded, false otherwise</returns>
        public async Task<bool> AddDocumentAsync(string docId, Dictionary<string, RedisValue> fields, double score = 1.0, bool noSave = false, bool replace = false, byte[] payload = null)
        {
            var args = BuildAddDocumentArgs(docId, fields, score, noSave, replace, payload);

            try
            {
                return (string)await _db.ExecuteAsync("FT.ADD", args).ConfigureAwait(false) == "OK";
            }
            catch (RedisServerException ex) when (ex.Message == "Document already in index")
            {
                return false;
            }
        }

        /// <summary>
        /// Add a document to the index
        /// </summary>
        /// <param name="doc">The document to add</param>
        /// <param name="options">Options for the operation</param>
        /// <returns>true if the operation succeeded, false otherwise</returns>
        public bool AddDocument(Document doc, AddOptions options = null)
        {
            var args = BuildAddDocumentArgs(doc.Id, doc._properties, doc.Score, options?.NoSave ?? false, options?.ReplacePolicy ?? AddOptions.ReplacementPolicy.None, doc.Payload, options?.Language);

            try
            {
                return (string)DbSync.Execute("FT.ADD", args) == "OK";
            }
            catch (RedisServerException ex) when (ex.Message == "Document already in index" || ex.Message == "Document already exists")
            {
                return false;
            }

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

        /// <summary>
        /// Add a batch of documents to the index.
        /// </summary>
        /// <param name="documents">The documents to add</param>
        /// <returns>`true` on success for each document</returns>
        public bool[] AddDocuments(params Document[] documents) =>
            AddDocuments(new AddOptions(), documents);

        /// <summary>
        /// Add a batch of documents to the index
        /// </summary>
        /// <param name="options">Options for the operation</param>
        /// <param name="documents">The documents to add</param>
        /// <returns>`true` on success for each document</returns>
        public bool[] AddDocuments(AddOptions options, params Document[] documents)
        {
            var result = new bool[documents.Length];

            for (var i = 0; i < documents.Length; i++)
            {
                result[i] = AddDocument(documents[i], options);
            }

            return result;
        }

        /// <summary>
        /// Add a batch of documents to the index.
        /// </summary>
        /// <param name="documents">The documents to add</param>
        /// <returns>`true` on success for each document</returns>
        public Task<bool[]> AddDocumentsAsync(params Document[] documents) =>
            AddDocumentsAsync(new AddOptions(), documents);

        /// <summary>
        /// Add a batch of documents to the index
        /// </summary>
        /// <param name="options">Options for the operation</param>
        /// <param name="documents">The documents to add</param>
        /// <returns>`true` on success for each document</returns>
        public async Task<bool[]> AddDocumentsAsync(AddOptions options, params Document[] documents)
        {
            var result = new bool[documents.Length];

            for (var i = 0; i < documents.Length; i++)
            {
                result[i] = await AddDocumentAsync(documents[i], options);
            }

            return result;
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
        /// [Deprecated] Use IDatabase.HashSet instead.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        [Obsolete("Use IDatabase.HashSet instead.")]
        public bool AddHash(string docId, double score, bool replace) => AddHash((RedisKey)docId, score, replace);

        /// <summary>
        /// Index a document already in redis as a HASH key.
        /// [Deprecated] Use IDatabase.HashSet instead.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        [Obsolete("Use IDatabase.HashSet instead.")]
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
        /// [Deprecated] Use IDatabase.HashSet instead.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        [Obsolete("Use IDatabase.HashSet instead.")]
        public Task<bool> AddHashAsync(string docId, double score, bool replace) => AddHashAsync((RedisKey)docId, score, replace);

        /// <summary>
        /// Index a document already in redis as a HASH key.
        /// [Deprecated] Use IDatabase.HashSet instead.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        [Obsolete("Use IDatabase.HashSet instead.")]
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
        /// Get the index info, including memory consumption and other statistics
        /// </summary>
        /// <returns>a map of key/value pairs</returns>
        public Dictionary<string, RedisValue> GetInfo() =>
            ParseGetInfo(DbSync.Execute("FT.INFO", _boxedIndexName));

        /// <summary>
        /// Get the index info, including memory consumption and other statistics
        /// </summary>
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
        /// Get the index info, including memory consumption and other statistics.
        /// </summary>
        /// <returns>An `InfoResult` object with parsed values from the FT.INFO command.</returns>
        public InfoResult GetInfoParsed() =>
            new InfoResult(DbSync.Execute("FT.INFO", _boxedIndexName));



        /// <summary>
        /// Get the index info, including memory consumption and other statistics.
        /// </summary>
        /// <returns>An `InfoResult` object with parsed values from the FT.INFO command.</returns>
        public async Task<InfoResult> GetInfoParsedAsync() =>
            new InfoResult(await _db.ExecuteAsync("FT.INFO", _boxedIndexName).ConfigureAwait(false));

        /// <summary>
        /// Delete a document from the index.
        /// </summary>
        /// <param name="docId">the document's id</param>
        /// <param name="deleteDocument">if <code>true</code> also deletes the actual document if it is in the index</param>
        /// <returns>true if it has been deleted, false if it did not exist</returns>
        public bool DeleteDocument(string docId, bool deleteDocument = false)
        {
            var args = new List<object>
            {
                _boxedIndexName,
                docId
            };

            if (deleteDocument)
            {
                args.Add("DD".Literal());
            }

            return (long)DbSync.Execute("FT.DEL", args) == 1;
        }

        /// <summary>
        /// Delete a document from the index.
        /// </summary>
        /// <param name="docId">the document's id</param>
        /// <param name="docId">the document's id</param>
        /// <returns>true if it has been deleted, false if it did not exist</returns>
        public async Task<bool> DeleteDocumentAsync(string docId, bool deleteDocument = false)
        {
            var args = new List<object>
            {
                _boxedIndexName,
                docId
            };

            if (deleteDocument)
            {
                args.Add("DD".Literal());
            }

            return (long)await _db.ExecuteAsync("FT.DEL", args).ConfigureAwait(false) == 1;
        }

        /// <summary>
        /// Delete multiple documents from an index. 
        /// </summary>
        /// <param name="deleteDocuments">if <code>true</code> also deletes the actual document ifs it is in the index</param>
        /// <param name="docIds">the document ids to delete</param>
        /// <returns>true on success for each document if it has been deleted, false if it did not exist</returns>
        public bool[] DeleteDocuments(bool deleteDocuments, params string[] docIds)
        {
            var result = new bool[docIds.Length];

            for (var i = 0; i < docIds.Length; i++)
            {
                result[i] = DeleteDocument(docIds[i], deleteDocuments);
            }

            return result;
        }

        /// <summary>
        /// Delete multiple documents from an index. 
        /// </summary>
        /// <param name="deleteDocuments">if <code>true</code> also deletes the actual document ifs it is in the index</param>
        /// <param name="docIds">the document ids to delete</param>
        /// <returns>true on success for each document if it has been deleted, false if it did not exist</returns>
        public async Task<bool[]> DeleteDocumentsAsync(bool deleteDocuments, params string[] docIds)
        {
            var result = new bool[docIds.Length];

            for (var i = 0; i < docIds.Length; i++)
            {
                result[i] = await DeleteDocumentAsync(docIds[i], deleteDocuments);
            }

            return result;
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
        /// [Deprecated] Optimize memory consumption of the index by removing extra saved capacity. This does not affect speed
        /// </summary>
        [Obsolete("Index optimizations are done by the internal garbage collector in the background.")]
        public long OptimizeIndex()
        {
            return default;
        }

        /// <summary>
        /// [Deprecated] Optimize memory consumption of the index by removing extra saved capacity. This does not affect speed
        /// </summary>
        [Obsolete("Index optimizations are done by the internal garbage collector in the background.")]
        public Task<long> OptimizeIndexAsync()
        {
            return Task.FromResult(default(long));
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
        /// <param name="suggestion">the Suggestion to be added</param>
        /// <param name="increment">if set, we increment the existing entry of the suggestion by the given score, instead of replacing the score. This is useful for updating the dictionary based on user queries in real time</param>
        /// <returns>the current size of the suggestion dictionary.</returns>
        public long AddSuggestion(Suggestion suggestion, bool increment = false)
        {
            var args = new List<object>
            {
                _boxedIndexName,
                suggestion.String,
                suggestion.Score
            };

            if (increment)
            {
                args.Add("INCR".Literal());
            }

            if (suggestion.Payload != null)
            {
                args.Add("PAYLOAD".Literal());
                args.Add(suggestion.Payload);
            }

            return (long)DbSync.Execute("FT.SUGADD", args);
        }

        /// <summary>
        /// Add a suggestion string to an auto-complete suggestion dictionary. This is disconnected from the index definitions, and leaves creating and updating suggestino dictionaries to the user.
        /// </summary>
        /// <param name="suggestion">the Suggestion to be added</param>
        /// <param name="increment">if set, we increment the existing entry of the suggestion by the given score, instead of replacing the score. This is useful for updating the dictionary based on user queries in real time</param>
        /// <returns>the current size of the suggestion dictionary.</returns>
        public async Task<long> AddSuggestionAsync(Suggestion suggestion, bool increment = false)
        {
            var args = new List<object>
            {
                _boxedIndexName,
                suggestion.String,
                suggestion.Score
            };

            if (increment)
            {
                args.Add("INCR".Literal());
            }

            if (suggestion.Payload != null)
            {
                args.Add("PAYLOAD".Literal());
                args.Add(suggestion.Payload);
            }

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
            var optionsBuilder = SuggestionOptions.Builder.Max(max);

            if (fuzzy)
            {
                optionsBuilder.Fuzzy();
            }

            var suggestions = GetSuggestions(prefix, optionsBuilder.Build());

            var result = new string[suggestions.Length];

            for (var i = 0; i < suggestions.Length; i++)
            {
                result[i] = suggestions[i].String;
            }

            return result;
        }

        /// <summary>
        /// Get completion suggestions for a prefix
        /// </summary>
        /// <param name="prefix">the prefix to complete on</param>
        /// <param name="suggestionOptions"> the options on what you need returned and other usage</param>
        /// <returns>a list of the top suggestions matching the prefix</returns>
        public Suggestion[] GetSuggestions(string prefix, SuggestionOptions options)
        {
            var args = new List<object>
            {
                _boxedIndexName,
                prefix,
                "MAX".Literal(),
                options.Max.Boxed()
            };

            if (options.Fuzzy)
            {
                args.Add("FUZZY".Literal());
            }

            if (options.With != WithOptions.None)
            {
                args.AddRange(options.GetFlags());
            }

            var results = (RedisResult[])DbSync.Execute("FT.SUGGET", args);

            if (options.With == WithOptions.None)
            {
                return GetSuggestionsNoOptions(results);
            }

            if (options.GetIsPayloadAndScores())
            {
                return GetSuggestionsWithPayloadAndScores(results);
            }

            if (options.GetIsPayload())
            {
                return GetSuggestionsWithPayload(results);
            }

            if (options.GetIsScores())
            {
                return GetSuggestionsWithScores(results);
            }

            return default;
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
            var optionsBuilder = SuggestionOptions.Builder.Max(max);

            if (fuzzy)
            {
                optionsBuilder.Fuzzy();
            }

            var suggestions = await GetSuggestionsAsync(prefix, optionsBuilder.Build());

            var result = new string[suggestions.Length];

            for(var i = 0; i < suggestions.Length; i++)
            {
                result[i] = suggestions[i].String;
            }

            return result;
        }


        /// <summary>
        /// Get completion suggestions for a prefix
        /// </summary>
        /// <param name="prefix">the prefix to complete on</param>
        /// <param name="suggestionOptions"> the options on what you need returned and other usage</param>
        /// <returns>a list of the top suggestions matching the prefix</returns>
        public async Task<Suggestion[]> GetSuggestionsAsync(string prefix, SuggestionOptions options)
        {
            var args = new List<object>
            {
                _boxedIndexName,
                prefix,
                "MAX".Literal(),
                options.Max.Boxed()
            };

            if (options.Fuzzy)
            {
                args.Add("FUZZY".Literal());
            }

            if (options.With != WithOptions.None)
            {
                args.AddRange(options.GetFlags());
            }

            var results = (RedisResult[])await _db.ExecuteAsync("FT.SUGGET", args).ConfigureAwait(false);

            if (options.With == WithOptions.None)
            {
                return GetSuggestionsNoOptions(results);
            }

            if (options.GetIsPayloadAndScores())
            {
                return GetSuggestionsWithPayloadAndScores(results);
            }

            if (options.GetIsPayload())
            {
                return GetSuggestionsWithPayload(results);
            }

            if (options.GetIsScores())
            {
                return GetSuggestionsWithScores(results);
            }

            return default;
        }

        /// <summary>
        /// Perform an aggregate query
        /// </summary>
        /// <param name="query">The query to watch</param>
        [Obsolete("Use `Aggregate` method that takes an `AggregationBuilder`.")]
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
        [Obsolete("Use `AggregateAsync` method that takes an `AggregationBuilder`.")]
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
        /// Perform an aggregate query
        /// </summary>
        /// <param name="query">The query to watch</param>
        public AggregationResult Aggregate(AggregationBuilder query)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };

            query.SerializeRedisArgs(args);

            var resp = DbSync.Execute("FT.AGGREGATE", args);

            if (query.IsWithCursor)
            {
                var respArray = (RedisResult[])resp;

                return new AggregationResult(respArray[0], (long)respArray[1]);
            }
            else
            {
                return new AggregationResult(resp);
            }
        }

        /// <summary>
        /// Perform an aggregate query
        /// </summary>
        /// <param name="query">The query to watch</param>
        public async Task<AggregationResult> AggregateAsync(AggregationBuilder query)
        {
            var args = new List<object>
            {
                _boxedIndexName
            };

            query.SerializeRedisArgs(args);

            var resp = await _db.ExecuteAsync("FT.AGGREGATE", args).ConfigureAwait(false);

            if (query.IsWithCursor)
            {
                var respArray = (RedisResult[])resp;

                return new AggregationResult(respArray[0], (long)respArray[1]);
            }
            else
            {
                return new AggregationResult(resp);
            }
        }

        /// <summary>
        /// Read from an existing aggregate cursor.
        /// </summary>
        /// <param name="cursorId">The cursor's ID.</param>
        /// <param name="count">Limit the amount of returned results.</param>
        /// <returns>A AggregationResult object with the results</returns>
        public AggregationResult CursorRead(long cursorId, int count = -1)
        {
            var args = new List<object>
            {
                "READ",
                _boxedIndexName,
                cursorId

            };

            if (count > -1)
            {
                args.Add("COUNT");
                args.Add(count);
            }

            RedisResult[] resp = (RedisResult[])DbSync.Execute("FT.CURSOR", args);

            return new AggregationResult(resp[0], (long)resp[1]);
        }

        /// <summary>
        /// Read from an existing aggregate cursor.
        /// </summary>
        /// <param name="cursorId">The cursor's ID.</param>
        /// <param name="count">Limit the amount of returned results.</param>
        /// <returns>A AggregationResult object with the results</returns>
        public async Task<AggregationResult> CursorReadAsync(long cursorId, int count)
        {
            var args = new List<object>
            {
                "READ",
                _boxedIndexName,
                cursorId

            };

            if (count > -1)
            {
                args.Add("COUNT");
                args.Add(count);
            }

            RedisResult[] resp = (RedisResult[])(await _db.ExecuteAsync("FT.CURSOR", args).ConfigureAwait(false));

            return new AggregationResult(resp[0], (long)resp[1]);
        }

        /// <summary>
        /// Delete a cursor from the index.
        /// </summary>
        /// <param name="cursorId">The cursor's ID.</param>
        /// <returns>`true` if it has been deleted, `false` if it did not exist.</returns>
        public bool CursorDelete(long cursorId)
        {
            var args = new List<object>
            {
                "DEL",
                _boxedIndexName,
                cursorId
            };

            return (string)DbSync.Execute("FT.CURSOR", args) == "OK";
        }

        /// <summary>
        /// Delete a cursor from the index.
        /// </summary>
        /// <param name="cursorId">The cursor's ID.</param>
        /// <returns>`true` if it has been deleted, `false` if it did not exist.</returns>
        public async Task<bool> CursorDeleteAsync(long cursorId)
        {
            var args = new List<object>
            {
                "DEL",
                _boxedIndexName,
                cursorId
            };

            return (string)(await _db.ExecuteAsync("FT.CURSOR", args).ConfigureAwait(false)) == "OK";
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
        /// Gets a series of documents from the index.
        /// </summary>
        /// <param name="docIds">The document IDs to retrieve.</param>
        /// <returns>The documents stored in the index. If the document does not exist, null is returned in the list.</returns>
        public Document[] GetDocuments(params string[] docIds)
        {
            if (docIds.Length == 0)
            {
                return Array.Empty<Document>();
            }

            var args = new List<object>
            {
                _boxedIndexName
            };

            foreach (var docId in docIds)
            {
                args.Add(docId);
            }

            var queryResults = (RedisResult[])DbSync.Execute("FT.MGET", args);

            var result = new Document[docIds.Length];

            for (var i = 0; i < docIds.Length; i++)
            {
                var queryResult = queryResults[i];

                if (queryResult.IsNull)
                {
                    result[i] = null;
                }
                else
                {
                    result[i] = Document.Parse(docIds[i], queryResult);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a series of documents from the index.
        /// </summary>
        /// <param name="docIds">The document IDs to retrieve.</param>
        /// <returns>The documents stored in the index. If the document does not exist, null is returned in the list.</returns>
        public async Task<Document[]> GetDocumentsAsync(params string[] docIds)
        {
            if (docIds.Length == 0)
            {
                return new Document[] { };
            }

            var args = new List<object>
            {
                _boxedIndexName
            };

            foreach (var docId in docIds)
            {
                args.Add(docId);
            }

            var queryResults = (RedisResult[])await _db.ExecuteAsync("FT.MGET", args).ConfigureAwait(false);

            var result = new Document[docIds.Length];

            for (var i = 0; i < docIds.Length; i++)
            {
                var queryResult = queryResults[i];

                if (queryResult.IsNull)
                {
                    result[i] = null;
                }
                else
                {
                    result[i] = Document.Parse(docIds[i], queryResult);
                }
            }

            return result;
        }

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
            return (string)await _db.ExecuteAsync("FT.ADD", args).ConfigureAwait(false) == "OK";
        }

        private static Suggestion[] GetSuggestionsNoOptions(RedisResult[] results)
        {
            var suggestions = new Suggestion[results.Length];

            for (var i = 0; i < results.Length; i++)
            {
                suggestions[i] = Suggestion.Builder.String((string)results[i]).Build(true);
            }

            return suggestions;
        }

        private static Suggestion[] GetSuggestionsWithPayloadAndScores(RedisResult[] results)
        {
            var suggestions = new Suggestion[results.Length / 3];

            for (var i = 3; i <= results.Length; i += 3)
            {
                var suggestion = Suggestion.Builder;

                suggestion.String((string)results[i - 3]);
                suggestion.Score((double)results[i - 2]);
                suggestion.Payload((string)results[i - 1]);

                suggestions[(i / 3) - 1] = suggestion.Build(true);
            }

            return suggestions;
        }

        private static Suggestion[] GetSuggestionsWithPayload(RedisResult[] results)
        {
            var suggestions = new Suggestion[results.Length / 2];

            for (var i = 2; i <= results.Length; i += 2)
            {
                var suggestion = Suggestion.Builder;

                suggestion.String((string)results[i - 2]);
                suggestion.Payload((string)results[i - 1]);

                suggestions[(i / 2) - 1] = suggestion.Build(true);
            }

            return suggestions;
        }

        private static Suggestion[] GetSuggestionsWithScores(RedisResult[] results)
        {
            var suggestions = new Suggestion[results.Length / 2];

            for (var i = 2; i <= results.Length; i += 2)
            {
                var suggestion = Suggestion.Builder;

                suggestion.String((string)results[i - 2]);
                suggestion.Score((double)results[i - 1]);

                suggestions[(i / 2) - 1] = suggestion.Build(true);
            }

            return suggestions;
        }
    }
}
