// .NET port of https://github.com/RedisLabs/JRediSearch/

using StackExchange.Redis;
using System;
using System.Collections.Generic;

namespace NRediSearch
{
    public sealed class Client
    {
        internal static readonly LiteralCache Literals = new LiteralCache();

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
                args.Add(Literals["NOOFFSETS"]);
            }
            if ((flags & IndexOptions.KeepFieldFlags) == 0)
            {
                args.Add(Literals["NOFIELDS"]);
            }
            if ((flags & IndexOptions.UseScoreIndexes) == 0)
            {
                args.Add(Literals["NOSCOREIDX"]);
            }
        }
        IDatabase _db;
        private object _boxedIndexName;
        public RedisKey IndexName => (RedisKey)_boxedIndexName;
        public Client(RedisKey indexName, IDatabase db)
        {
            _db = db;
            _boxedIndexName = indexName; // only box once, not per-command
        }

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
            args.Add(Literals["SCHEMA"]);

            foreach (var f in schema.Fields)
            {
                f.SerializeRedisArgs(args);
            }

            return (string)_db.Execute("FT.CREATE", args.ToArray()) == "OK";
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

            var resp = (RedisResult[])_db.Execute("FT.SEARCH", args.ToArray());
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
        public bool AddDocument(string docId, double score, Dictionary<string, RedisValue> fields, bool noSave, bool replace, byte[] payload)
        {
            var args = new List<object> { _boxedIndexName, docId, score };
            if (noSave)
            {
                args.Add(Literals["NOSAVE"]);
            }
            if (replace)
            {
                args.Add(Literals["REPLACE"]);
            }
            if (payload != null)
            {
                args.Add(Literals["PAYLOAD"]);
                // TODO: Fix this
                args.Add(payload);
            }

            args.Add(Literals["FIELDS"]);
            foreach (var ent in fields)
            {
                args.Add(ent.Key);
                args.Add(ent.Value);
            }

            return (string)_db.Execute("FT.ADD", args.ToArray()) == "OK";
        }

        /// <summary>
        /// replaceDocument is a convenience for calling addDocument with replace=true 
        /// </summary>
        public bool ReplaceDocument(string docId, double score, Dictionary<string, RedisValue> fields)
            => AddDocument(docId, score, fields, false, true, null);

        /** See above */
        public bool AddDocument(string docId, double score, Dictionary<string, RedisValue> fields)
            => AddDocument(docId, score, fields, false, false, null);
        /** See above */
        public bool AddDocument(string docId, Dictionary<string, RedisValue> fields)
            => AddDocument(docId, 1, fields, false, false, null);

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
                args.Add(Literals["REPLACE"]);
            }

            return (string)_db.Execute("FT.ADDHASH", args.ToArray()) == "OK";
        }

        /// <summary>
        /// Get the index info, including memory consumption and other statistics.
        /// </summary>
        /// <remarks>TODO: Make a class for easier access to the index properties</remarks>
        /// <returns>a map of key/value pairs</returns>
        public Dictionary<string, RedisValue> GetInfo()
        {

            var res = (RedisValue[])_db.Execute("FT.INFO", _boxedIndexName);
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
            long r = (long)_db.Execute("FT.DEL", _boxedIndexName, docId);
            return r == 1;
        }

        /// <summary>
        /// Drop the index and all associated keys, including documents
        /// </summary>
        /// <returns>true on success</returns>
        public bool DropIndex()
        {
            return (string)_db.Execute("FT.DROP", _boxedIndexName) == "OK";
        }

        /// <summary>
        /// Optimize memory consumption of the index by removing extra saved capacity. This does not affect speed
        /// </summary>
        public long OptimizeIndex()
        {
            long ret = (long)_db.Execute("FT.OPTIMIZE", _boxedIndexName);
            return ret;
        }
    }
}
