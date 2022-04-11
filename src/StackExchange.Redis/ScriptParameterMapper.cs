using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace StackExchange.Redis
{
    internal static class ScriptParameterMapper
    {
        public readonly struct ScriptParameters
        {
            public readonly RedisKey[] Keys;
            public readonly RedisValue[] Arguments;

            public static readonly ConstructorInfo Cons = typeof(ScriptParameters).GetConstructor(new[] { typeof(RedisKey[]), typeof(RedisValue[]) })!;
            public ScriptParameters(RedisKey[] keys, RedisValue[] args)
            {
                Keys = keys;
                Arguments = args;
            }
        }

        private static readonly Regex ParameterExtractor = new Regex(@"@(?<paramName> ([a-z]|_) ([a-z]|_|\d)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

        private static bool TryExtractParameters(string script, [NotNullWhen(true)] out string[]? parameters)
        {
            var ps = ParameterExtractor.Matches(script);
            if (ps.Count == 0)
            {
                parameters = null;
                return false;
            }

            var ret = new HashSet<string>();

            for (var i = 0; i < ps.Count; i++)
            {
                var c = ps[i];
                var ix = c.Index - 1;
                if (ix >= 0)
                {
                    var prevChar = script[ix];

                    // don't consider this a parameter if it's in the middle of word (i.e. if it's preceded by a letter)
                    if (char.IsLetterOrDigit(prevChar) || prevChar == '_') continue;

                    // this is an escape, ignore it
                    if (prevChar == '@') continue;
                }

                var n = c.Groups["paramName"].Value;
                if (!ret.Contains(n)) ret.Add(n);
            }

            parameters = ret.ToArray();
            return true;
        }

        private static string MakeOrdinalScriptWithoutKeys(string rawScript, string[] args)
        {
            var ps = ParameterExtractor.Matches(rawScript);
            if (ps.Count == 0) return rawScript;

            var ret = new StringBuilder();
            var upTo = 0;

            for (var i = 0; i < ps.Count; i++)
            {
                var capture = ps[i];
                var name = capture.Groups["paramName"].Value;

                var ix = capture.Index;
                ret.Append(rawScript, upTo, ix - upTo);

                var argIx = Array.IndexOf(args, name);

                if (argIx != -1)
                {
                    ret.Append("ARGV[");
                    ret.Append(argIx + 1);
                    ret.Append(']');
                }
                else
                {
                    var isEscape = false;
                    var prevIx = capture.Index - 1;
                    if (prevIx >= 0)
                    {
                        var prevChar = rawScript[prevIx];
                        isEscape = prevChar == '@';
                    }

                    if (isEscape)
                    {
                        // strip the @ off, so just the one triggering the escape exists
                        ret.Append(capture.Groups["paramName"].Value);
                    }
                    else
                    {
                        ret.Append(capture.Value);
                    }
                }

                upTo = capture.Index + capture.Length;
            }

            ret.Append(rawScript, upTo, rawScript.Length - upTo);

            return ret.ToString();
        }

        private static readonly Dictionary<Type, MethodInfo> _conversionOperators;
        static ScriptParameterMapper()
        {
            var tmp = new Dictionary<Type, MethodInfo>();
            foreach (var method in typeof(RedisValue).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.ReturnType == typeof(RedisValue) && (method.Name == "op_Implicit" || method.Name == "op_Explicit"))
                {
                    var p = method.GetParameters();
                    if (p?.Length == 1)
                    {
                        tmp[p[0].ParameterType] = method;
                    }
                }
            }
            _conversionOperators = tmp;
        }

        /// <summary>
        /// Turns a script with @namedParameters into a LuaScript that can be executed against a given IDatabase(Async) object.
        /// </summary>
        /// <param name="script">The script to prepare.</param>
        public static LuaScript PrepareScript(string script)
        {
            if (TryExtractParameters(script, out var ps))
            {
                var ordinalScript = MakeOrdinalScriptWithoutKeys(script, ps);
                return new LuaScript(script, ordinalScript, ps);
            }
            throw new ArgumentException("Count not parse script: " + script);
        }

        private static readonly HashSet<Type> ConvertableTypes = new()
        {
            typeof(int),
            typeof(int?),
            typeof(long),
            typeof(long?),
            typeof(double),
            typeof(double?),
            typeof(string),
            typeof(byte[]),
            typeof(ReadOnlyMemory<byte>),
            typeof(bool),
            typeof(bool?),

            typeof(RedisKey),
            typeof(RedisValue)
        };

        /// <summary>
        /// Determines whether or not the given type can be used to provide parameters for the given <see cref="LuaScript"/>.
        /// </summary>
        /// <param name="t">The type of the parameter.</param>
        /// <param name="script">The script to match against.</param>
        /// <param name="missingMember">The first missing member, if any.</param>
        /// <param name="badTypeMember">The first type mismatched member, if any.</param>
        public static bool IsValidParameterHash(Type t, LuaScript script, out string? missingMember, out string? badTypeMember)
        {
            for (var i = 0; i < script.Arguments.Length; i++)
            {
                var argName = script.Arguments[i];
                var member = t.GetMember(argName).SingleOrDefault(m => m is PropertyInfo || m is FieldInfo);
                if (member == null)
                {
                    missingMember = argName;
                    badTypeMember = null;
                    return false;
                }

                var memberType = member is FieldInfo memberFieldInfo ? memberFieldInfo.FieldType : ((PropertyInfo)member).PropertyType;
                if (!ConvertableTypes.Contains(memberType))
                {
                    missingMember = null;
                    badTypeMember = argName;
                    return false;
                }
            }

            missingMember = badTypeMember = null;
            return true;
        }

        /// <summary>
        /// <para>Creates a Func that extracts parameters from the given type for use by a LuaScript.</para>
        /// <para>
        /// Members that are RedisKey's get extracted to be passed in as keys to redis; all members that
        /// appear in the script get extracted as RedisValue arguments to be sent up as args.
        /// </para>
        /// <para>
        /// We send all values as arguments so we don't have to prepare the same script for different parameter
        /// types.
        /// </para>
        /// <para>
        /// The created Func takes a RedisKey, which will be prefixed to all keys (and arguments of type RedisKey) for
        /// keyspace isolation.
        /// </para>
        /// </summary>
        /// <param name="t">The type to extract for.</param>
        /// <param name="script">The script to extract for.</param>
        public static Func<object, RedisKey?, ScriptParameters> GetParameterExtractor(Type t, LuaScript script)
        {
            if (!IsValidParameterHash(t, script, out _, out _)) throw new Exception("Shouldn't be possible");

            static Expression GetMember(Expression root, MemberInfo member) => member.MemberType switch
            {
                MemberTypes.Property => Expression.Property(root, (PropertyInfo)member),
                MemberTypes.Field => Expression.Field(root, (FieldInfo)member),
                _ => throw new ArgumentException($"Member type '{member.MemberType}' isn't recognized", nameof(member)),
            };
            var keys = new List<MemberInfo>();
            var args = new List<MemberInfo>();

            for (var i = 0; i < script.Arguments.Length; i++)
            {
                var argName = script.Arguments[i];
                var member = t.GetMember(argName).SingleOrDefault(m => m is PropertyInfo || m is FieldInfo);
                if (member is null)
                {
                    throw new ArgumentException($"There was no member found for {argName}");
                }

                var memberType = member is FieldInfo memberFieldInfo ? memberFieldInfo.FieldType : ((PropertyInfo)member).PropertyType;

                if (memberType == typeof(RedisKey))
                {
                    keys.Add(member);
                }
                else if (memberType != typeof(RedisValue) && !_conversionOperators.ContainsKey(memberType))
                {
                    throw new InvalidCastException($"There is no conversion available from {memberType.Name} to {nameof(RedisValue)}");
                }
                args.Add(member);
            }

            var objUntyped = Expression.Parameter(typeof(object), "obj");
            var objTyped = Expression.Convert(objUntyped, t);
            var keyPrefix = Expression.Parameter(typeof(RedisKey?), "keyPrefix");

            Expression keysResult, valuesResult;
            MethodInfo? asRedisValue = null;
            Expression[]? keysResultArr = null;
            if (keys.Count == 0)
            {
                // if there are no keys, don't allocate
                keysResult = Expression.Constant(null, typeof(RedisKey[]));
            }
            else
            {
                var needsKeyPrefix = Expression.Property(keyPrefix, nameof(Nullable<RedisKey>.HasValue));
                var keyPrefixValueArr = new[] { Expression.Call(keyPrefix,
                    nameof(Nullable<RedisKey>.GetValueOrDefault), null, null) };
                var prepend = typeof(RedisKey).GetMethod(nameof(RedisKey.Prepend),
                    BindingFlags.Public | BindingFlags.Instance)!;
                asRedisValue = typeof(RedisKey).GetMethod(nameof(RedisKey.AsRedisValue),
                    BindingFlags.NonPublic | BindingFlags.Instance)!;

                keysResultArr = new Expression[keys.Count];
                for (int i = 0; i < keysResultArr.Length; i++)
                {
                    var member = GetMember(objTyped, keys[i]);
                    keysResultArr[i] = Expression.Condition(needsKeyPrefix,
                        Expression.Call(member, prepend, keyPrefixValueArr),
                        member);
                }
                keysResult = Expression.NewArrayInit(typeof(RedisKey), keysResultArr);
            }

            if (args.Count == 0)
            {
                // if there are no args, don't allocate
                valuesResult = Expression.Constant(null, typeof(RedisValue[]));
            }
            else
            {
                valuesResult = Expression.NewArrayInit(typeof(RedisValue), args.Select(arg =>
                {
                    var member = GetMember(objTyped, arg);
                    if (member.Type == typeof(RedisValue)) return member; // pass-through
                    if (member.Type == typeof(RedisKey))
                    { // need to apply prefix (note we can re-use the body from earlier)
                        var val = keysResultArr![keys.IndexOf(arg)];
                        return Expression.Call(val, asRedisValue!);
                    }

                    // otherwise: use the conversion operator
                    var conversion = _conversionOperators[member.Type];
                    return Expression.Call(conversion, member);
                }));
            }

            var body = Expression.Lambda<Func<object, RedisKey?, ScriptParameters>>(
                Expression.New(ScriptParameters.Cons, keysResult, valuesResult),
                objUntyped, keyPrefix);
            return body.Compile();
        }
    }
}
