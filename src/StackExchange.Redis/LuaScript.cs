using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// <para>Represents a Lua script that can be executed on Redis.</para>
    /// <para>
    /// Unlike normal Redis Lua scripts, LuaScript can have named parameters (prefixed by a @).
    /// Public fields and properties of the passed in object are treated as parameters.
    /// </para>
    /// <para>
    /// Parameters of type RedisKey are sent to Redis as KEY (https://redis.io/commands/eval) in addition to arguments,
    /// so as to play nicely with Redis Cluster.
    /// </para>
    /// <para>All members of this class are thread safe.</para>
    /// </summary>
    public sealed class LuaScript
    {
        /// <summary>
        /// Since the mapping of "script text" -> LuaScript doesn't depend on any particular details of
        /// the redis connection itself, this cache is global.
        /// </summary>
        private static readonly ConcurrentDictionary<string, WeakReference> Cache = new();

        /// <summary>
        /// The original Lua script that was used to create this.
        /// </summary>
        public string OriginalScript { get; }

        /// <summary>
        /// <para>The Lua script that will actually be sent to Redis for execution.</para>
        /// <para>All @-prefixed parameter names have been replaced at this point.</para>
        /// </summary>
        public string ExecutableScript { get; }

        /// <summary>
        /// Arguments are in the order they have to passed to the script in.
        /// </summary>
        internal string[] Arguments { get; }

        private bool HasArguments => Arguments?.Length > 0;

        private readonly Hashtable? ParameterMappers;

        internal LuaScript(string originalScript, string executableScript, string[] arguments)
        {
            OriginalScript = originalScript;
            ExecutableScript = executableScript;
            Arguments = arguments;

            if (HasArguments)
            {
                ParameterMappers = new Hashtable();
            }
        }

        /// <summary>
        /// Finalizer - used to prompt cleanups of the script cache when a LuaScript reference goes out of scope.
        /// </summary>
        ~LuaScript()
        {
            try
            {
                Cache.TryRemove(OriginalScript, out _);
            }
            catch { }
        }

        /// <summary>
        /// Invalidates the internal cache of LuaScript objects.
        /// Existing LuaScripts will continue to work, but future calls to LuaScript.Prepare
        /// return a new LuaScript instance.
        /// </summary>
        public static void PurgeCache() => Cache.Clear();

        /// <summary>
        /// Returns the number of cached LuaScripts.
        /// </summary>
        public static int GetCachedScriptCount() => Cache.Count;

        /// <summary>
        /// Prepares a Lua script with named parameters to be run against any Redis instance.
        /// </summary>
        /// <param name="script">The script to prepare.</param>
        public static LuaScript Prepare(string script)
        {
            if (!Cache.TryGetValue(script, out WeakReference? weakRef) || weakRef.Target is not LuaScript ret)
            {
                ret = ScriptParameterMapper.PrepareScript(script);
                Cache[script] = new WeakReference(ret);
            }

            return ret;
        }

        internal void ExtractParameters(object? ps, RedisKey? keyPrefix, out RedisKey[]? keys, out RedisValue[]? args)
        {
            if (HasArguments)
            {
                if (ps == null) throw new ArgumentNullException(nameof(ps), "Script requires parameters");

                var psType = ps.GetType();
                var mapper = (Func<object, RedisKey?, ScriptParameterMapper.ScriptParameters>?)ParameterMappers![psType];
                if (mapper == null)
                {
                    lock (ParameterMappers)
                    {
                        mapper = (Func<object, RedisKey?, ScriptParameterMapper.ScriptParameters>?)ParameterMappers[psType];
                        if (mapper == null)
                        {
                            if (!ScriptParameterMapper.IsValidParameterHash(psType, this, out string? missingMember, out string? badMemberType))
                            {
                                if (missingMember != null)
                                {
                                    throw new ArgumentException("Expected [" + missingMember + "] to be a field or gettable property on [" + psType.FullName + "]", nameof(ps));
                                }

                                throw new ArgumentException("Expected [" + badMemberType + "] on [" + psType.FullName + "] to be convertible to a RedisValue", nameof(ps));
                            }

                            ParameterMappers[psType] = mapper = ScriptParameterMapper.GetParameterExtractor(psType, this);
                        }
                    }
                }

                var mapped = mapper(ps, keyPrefix);
                keys = mapped.Keys;
                args = mapped.Arguments;
            }
            else
            {
                keys = null;
                args = null;
            }
        }

        /// <summary>
        /// Evaluates this LuaScript against the given database, extracting parameters from the passed in object if any.
        /// </summary>
        /// <param name="db">The redis database to evaluate against.</param>
        /// <param name="ps">The parameter object to use.</param>
        /// <param name="withKeyPrefix">The key prefix to use, if any.</param>
        /// <param name="flags">The command flags to use.</param>
        public RedisResult Evaluate(IDatabase db, object? ps = null, RedisKey? withKeyPrefix = null, CommandFlags flags = CommandFlags.None)
        {
            ExtractParameters(ps, withKeyPrefix, out RedisKey[]? keys, out RedisValue[]? args);
            return db.ScriptEvaluate(ExecutableScript, keys, args, flags);
        }

        /// <summary>
        /// Evaluates this LuaScript against the given database, extracting parameters from the passed in object if any.
        /// </summary>
        /// <param name="db">The redis database to evaluate against.</param>
        /// <param name="ps">The parameter object to use.</param>
        /// <param name="withKeyPrefix">The key prefix to use, if any.</param>
        /// <param name="flags">The command flags to use.</param>
        public Task<RedisResult> EvaluateAsync(IDatabaseAsync db, object? ps = null, RedisKey? withKeyPrefix = null, CommandFlags flags = CommandFlags.None)
        {
            ExtractParameters(ps, withKeyPrefix, out RedisKey[]? keys, out RedisValue[]? args);
            return db.ScriptEvaluateAsync(ExecutableScript, keys, args, flags);
        }

        /// <summary>
        /// <para>
        /// Loads this LuaScript into the given IServer so it can be run with it's SHA1 hash, instead of
        /// passing the full script on each Evaluate or EvaluateAsync call.
        /// </para>
        /// <para>Note: the FireAndForget command flag cannot be set.</para>
        /// </summary>
        /// <param name="server">The server to load the script on.</param>
        /// <param name="flags">The command flags to use.</param>
        public LoadedLuaScript Load(IServer server, CommandFlags flags = CommandFlags.None)
        {
            if ((flags & CommandFlags.FireAndForget) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(flags), "Loading a script cannot be FireAndForget");
            }

            var hash = server.ScriptLoad(ExecutableScript, flags);
            return new LoadedLuaScript(this, hash!); // not nullable because fire and forget is disabled
        }

        /// <summary>
        /// <para>
        /// Loads this LuaScript into the given IServer so it can be run with it's SHA1 hash, instead of
        /// passing the full script on each Evaluate or EvaluateAsync call.
        /// </para>
        /// <para>Note: the FireAndForget command flag cannot be set</para>
        /// </summary>
        /// <param name="server">The server to load the script on.</param>
        /// <param name="flags">The command flags to use.</param>
        public async Task<LoadedLuaScript> LoadAsync(IServer server, CommandFlags flags = CommandFlags.None)
        {
            if ((flags & CommandFlags.FireAndForget) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(flags), "Loading a script cannot be FireAndForget");
            }

            var hash = await server.ScriptLoadAsync(ExecutableScript, flags).ForAwait()!;
            return new LoadedLuaScript(this, hash!); // not nullable because fire and forget is disabled
        }
    }

    /// <summary>
    /// <para>Represents a Lua script that can be executed on Redis.</para>
    /// <para>
    /// Unlike LuaScript, LoadedLuaScript sends the hash of it's ExecutableScript to Redis rather than pass
    /// the whole script on each call.  This requires that the script be loaded into Redis before it is used.
    /// </para>
    /// <para>
    /// To create a LoadedLuaScript first create a LuaScript via LuaScript.Prepare(string), then
    /// call Load(IServer, CommandFlags) on the returned LuaScript.
    /// </para>
    /// <para>
    /// Unlike normal Redis Lua scripts, LoadedLuaScript can have named parameters (prefixed by a @).
    /// Public fields and properties of the passed in object are treated as parameters.
    /// </para>
    /// <para>
    /// Parameters of type RedisKey are sent to Redis as KEY (https://redis.io/commands/eval) in addition to arguments,
    /// so as to play nicely with Redis Cluster.
    /// </para>
    /// <para>All members of this class are thread safe.</para>
    /// </summary>
    public sealed class LoadedLuaScript
    {
        /// <summary>
        /// The original script that was used to create this LoadedLuaScript.
        /// </summary>
        public string OriginalScript => Original.OriginalScript;

        /// <summary>
        /// The script that will actually be sent to Redis for execution.
        /// </summary>
        public string ExecutableScript => Original.ExecutableScript;

        /// <summary>
        /// <para>The SHA1 hash of ExecutableScript.</para>
        /// <para>This is sent to Redis instead of ExecutableScript during Evaluate and EvaluateAsync calls.</para>
        /// </summary>
        public byte[] Hash { get; }

        // internal for testing purposes only
        internal LuaScript Original;

        internal LoadedLuaScript(LuaScript original, byte[] hash)
        {
            Original = original;
            Hash = hash;
        }

        /// <summary>
        /// <para>Evaluates this LoadedLuaScript against the given database, extracting parameters for the passed in object if any.</para>
        /// <para>
        /// This method sends the SHA1 hash of the ExecutableScript instead of the script itself.
        /// If the script has not been loaded into the passed Redis instance, it will fail.
        /// </para>
        /// </summary>
        /// <param name="db">The redis database to evaluate against.</param>
        /// <param name="ps">The parameter object to use.</param>
        /// <param name="withKeyPrefix">The key prefix to use, if any.</param>
        /// <param name="flags">The command flags to use.</param>
        public RedisResult Evaluate(IDatabase db, object? ps = null, RedisKey? withKeyPrefix = null, CommandFlags flags = CommandFlags.None)
        {
            Original.ExtractParameters(ps, withKeyPrefix, out RedisKey[]? keys, out RedisValue[]? args);
            return db.ScriptEvaluate(Hash, keys, args, flags);
        }

        /// <summary>
        /// <para>Evaluates this LoadedLuaScript against the given database, extracting parameters for the passed in object if any.</para>
        /// <para>
        /// This method sends the SHA1 hash of the ExecutableScript instead of the script itself.
        /// If the script has not been loaded into the passed Redis instance, it will fail.
        /// </para>
        /// </summary>
        /// <param name="db">The redis database to evaluate against.</param>
        /// <param name="ps">The parameter object to use.</param>
        /// <param name="withKeyPrefix">The key prefix to use, if any.</param>
        /// <param name="flags">The command flags to use.</param>
        public Task<RedisResult> EvaluateAsync(IDatabaseAsync db, object? ps = null, RedisKey? withKeyPrefix = null, CommandFlags flags = CommandFlags.None)
        {
            Original.ExtractParameters(ps, withKeyPrefix, out RedisKey[]? keys, out RedisValue[]? args);
            return db.ScriptEvaluateAsync(Hash, keys, args, flags);
        }
    }
}
