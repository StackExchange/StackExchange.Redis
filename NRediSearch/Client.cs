// .NET port of https://github.com/RedisLabs/JRediSearch/

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
            /// If set, we keep an index of the top entries per term, allowing extremely fast single word queries
            /// regardless of index size, at the cost of more memory
            /// </summary>
            UseScoreIndexes = 4,
            /// <summary>
            /// The default indexing options - use term offsets and keep fields flags
            /// </summary>
            Default = UseTermOffsets | KeepFieldFlags
        }
        private static void SerializeRedisArgs(IndexOptions flags, List<object> args)
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
            var args = new List<object>();

            args.Add(_boxedIndexName);
            SerializeRedisArgs(options, args);
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
            var args = new List<object>();

            args.Add(_boxedIndexName);
            SerializeRedisArgs(options, args);
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
            var args = new List<object>();
            args.Add(_boxedIndexName);
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
            var args = new List<object>();
            args.Add(_boxedIndexName);
            q.SerializeRedisArgs(args);

            var resp = (RedisResult[])await _db.ExecuteAsync("FT.SEARCH", args).ConfigureAwait(false);
            return new SearchResult(resp, !q.NoContent, q.WithScores, q.WithPayloads);
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

        private List<object> BuildAddDocumentArgs(string docId, Dictionary<string, RedisValue> fields, double score, bool noSave, bool replace, byte[] payload)
        {
            var args = new List<object> { _boxedIndexName, docId, score };
            if (noSave)
            {
                args.Add("NOSAVE".Literal());
            }
            if (replace)
            {
                args.Add("REPLACE".Literal());
            }
            if (payload != null)
            {
                args.Add("PAYLOAD".Literal());
                // TODO: Fix this
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
        /// replaceDocument is a convenience for calling addDocument with replace=true 
        /// </summary>
        public bool ReplaceDocument(string docId, Dictionary<string, RedisValue> fields, double score = 1.0, byte[] payload = null)
            => AddDocument(docId, fields, score, false, true, payload);

        /// <summary>
        /// replaceDocument is a convenience for calling addDocument with replace=true 
        /// </summary>
        public Task<bool> ReplaceDocumentAsync(string docId, Dictionary<string, RedisValue> fields, double score = 1.0, byte[] payload = null)
            => AddDocumentAsync(docId, fields, score, false, true, payload);

        /// <summary>
        /// Index a document already in redis as a HASH key.
        /// </summary>
        /// <param name="docId">the id of the document in redis. This must match an existing, unindexed HASH key</param>
        /// <param name="score">the document's index score, between 0 and 1</param>
        /// <param name="replace">if set, and the document already exists, we reindex and update it</param>
        /// <returns>true on success</returns>
        public bool AddHash(string docId, double score, bool replace)
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
        public async Task<bool> AddHashAsync(string docId, double score, bool replace)
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
        public Dictionary<string, RedisValue> GetInfo()
        {
            return ParseGetInfo(DbSync.Execute("FT.INFO", _boxedIndexName));
        }
        /// <summary>
        /// Get the index info, including memory consumption and other statistics.
        /// </summary>
        /// <remarks>TODO: Make a class for easier access to the index properties</remarks>
        /// <returns>a map of key/value pairs</returns>
        public async Task<Dictionary<string, RedisValue>> GetInfoAsync()
        {
            return ParseGetInfo(await _db.ExecuteAsync("FT.INFO", _boxedIndexName).ConfigureAwait(false));
        }
        static Dictionary<string, RedisValue> ParseGetInfo(RedisResult value)
        {
            var res = (RedisValue[])value;
            var info = new Dictionary<string, RedisValue>();
            for (int i = 0; i < res.Length; i += 2)
            {
                var key = (string)res[i];
                var val = res[i + 1];
                info.Add(key, val);
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
            return (string) await _db.ExecuteAsync("FT.DROP", _boxedIndexName).ConfigureAwait(false) == "OK";
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
            return (long) await _db.ExecuteAsync("FT.OPTIMIZE", _boxedIndexName).ConfigureAwait(false);
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
            object args = increment
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
            object args = increment
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
            var args = new List<object> { _boxedIndexName, prefix};
            if (fuzzy) args.Add("FUZZY".Literal());
            if (max != 5)
            {
                args.Add("MAX".Literal());
                args.Add(max);
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
                args.Add(max);
            }
            return (string[])await _db.ExecuteAsync("FT.SUGGET", args).ConfigureAwait(false);
        }
    }
}
