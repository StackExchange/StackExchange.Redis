using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
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
            public readonly RedisKey[] KeyArray;
            public readonly RedisValue[] ArgArray;
            public readonly int KeyCount;
            public readonly int ArgCount;

            public static readonly ConstructorInfo Constructor = typeof(ScriptParameters).GetConstructor(new[] { typeof(int), typeof(int) });
            public ScriptParameters(int keyCount, int argCount)
            {
                KeyCount = keyCount;
                KeyArray = keyCount == 0 ? Array.Empty<RedisKey>() : ArrayPool<RedisKey>.Shared.Rent(keyCount);
                ArgCount = argCount;
                ArgArray = argCount == 0 ? Array.Empty<RedisValue>() : ArrayPool<RedisValue>.Shared.Rent(argCount);
            }
            public ReadOnlyMemory<RedisKey> Keys => new ReadOnlyMemory<RedisKey>(KeyArray, 0, KeyCount);
            public ReadOnlyMemory<RedisValue> Args => new ReadOnlyMemory<RedisValue>(ArgArray, 0, ArgCount);
        }

        private static readonly Regex ParameterExtractor = new Regex(@"@(?<paramName> ([a-z]|_) ([a-z]|_|\d)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
        private static string[] ExtractParameters(string script)
        {
            var ps = ParameterExtractor.Matches(script);
            if (ps.Count == 0) return null;

            var ret = new HashSet<string>();

            for (var i = 0; i < ps.Count; i++)
            {
                var c = ps[i];
                var ix = c.Index - 1;
                if (ix >= 0)
                {
                    var prevChar = script[ix];

                    // don't consider this a parameter if it's in the middle of word (ie. if it's preceeded by a letter)
                    if (char.IsLetterOrDigit(prevChar) || prevChar == '_') continue;

                    // this is an escape, ignore it
                    if (prevChar == '@') continue;
                }

                var n = c.Groups["paramName"].Value;
                if (!ret.Contains(n)) ret.Add(n);
            }

            return ret.ToArray();
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
                    ret.Append("]");
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
        /// Turns a script with @namedParameters into a LuaScript that can be executed
        /// against a given IDatabase(Async) object
        /// </summary>
        /// <param name="script">The script to prepare.</param>
        public static LuaScript PrepareScript(string script)
        {
            var ps = ExtractParameters(script);
            var ordinalScript = MakeOrdinalScriptWithoutKeys(script, ps);

            return new LuaScript(script, ordinalScript, ps);
        }

        private static readonly HashSet<Type> ConvertableTypes =
            new HashSet<Type> {
                typeof(int),
                typeof(int?),
                typeof(long),
                typeof(long?),
                typeof(double),
                typeof(double?),
                typeof(string),
                typeof(byte[]),
                typeof(bool),
                typeof(bool?),

                typeof(RedisKey),
                typeof(RedisValue)
            };

        /// <summary>
        /// Determines whether or not the given type can be used to provide parameters for the given LuaScript.
        /// </summary>
        /// <param name="t">The type of the parameter.</param>
        /// <param name="script">The script to match against.</param>
        /// <param name="missingMember">The first missing member, if any.</param>
        /// <param name="badTypeMember">The first type mismatched member, if any.</param>
        public static bool IsValidParameterHash(Type t, LuaScript script, out string missingMember, out string badTypeMember)
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

                var memberType = member is FieldInfo ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;
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

        private static readonly MethodInfo s_prepend = typeof(RedisKey).GetMethod(nameof(RedisKey.Prepend), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo s_asRedisValue = typeof(RedisKey).GetMethod(nameof(RedisKey.AsRedisValue), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

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

            Expression GetMember(Expression root, MemberInfo member)
            {
                switch (member.MemberType)
                {
                    case MemberTypes.Property:
                        return Expression.Property(root, (PropertyInfo)member);
                    case MemberTypes.Field:
                        return Expression.Field(root, (FieldInfo)member);
                    default:
                        throw new ArgumentException(nameof(member));
                }
            }
            var keys = new List<MemberInfo>();
            var args = new List<MemberInfo>();

            for (var i = 0; i < script.Arguments.Length; i++)
            {
                var argName = script.Arguments[i];
                var member = t.GetMember(argName).SingleOrDefault(m => m is PropertyInfo || m is FieldInfo);

                var memberType = member is FieldInfo ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;

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

            // parameters
            var objUntyped = Expression.Parameter(typeof(object), "obj");
            var keyPrefix = Expression.Parameter(typeof(RedisKey?), "keyPrefix");

            // locals
            var operations = new List<Expression>();
            var objTyped = Expression.Variable(t);
            var result = Expression.Variable(typeof(ScriptParameters));
            var keyArr = Expression.Variable(typeof(RedisKey[]));
            var argArr = Expression.Variable(typeof(RedisValue[]));
            var needsPrefix = Expression.Variable(typeof(bool));
            var prefixValue = Expression.Variable(typeof(RedisKey));

            // objTyped = (t)objUntyped
            operations.Add(Expression.Assign(objTyped, Expression.Convert(objUntyped, t)));

            // result = new ScriptParameters(keys.Count, args.Count)
            operations.Add(Expression.Assign(result, Expression.New(ScriptParameters.Constructor, Expression.Constant(keys.Count), Expression.Constant(args.Count))));

            if (keys.Count != 0)
            {
                // keyArr = result.KeyArray;
                operations.Add(Expression.Assign(keyArr, Expression.PropertyOrField(result, nameof(ScriptParameters.KeyArray))));

                // needsPrefix = keyPrefix.HasValue
                // prefixValue = prefixValue.GetValueOrDefault()
                operations.Add(Expression.Assign(needsPrefix, Expression.PropertyOrField(keyPrefix, nameof(Nullable<RedisKey>.HasValue))));
                operations.Add(Expression.Assign(prefixValue, Expression.Call(keyPrefix, nameof(Nullable<RedisKey>.GetValueOrDefault), null, null)));

                var needsKeyPrefix = Expression.Property(keyPrefix, nameof(Nullable<RedisKey>.HasValue));
                var prefixValueAsArgs = new[] { prefixValue };

                int i = 0;
                foreach(var key in keys)
                {
                    // keyArr[i++] = needsKeyPrefix ? objTyped.{member} : objTyped.{Member}.Prepend(prefixValue)
                    var member = GetMember(objTyped, key);
                    operations.Add(Expression.Assign(Expression.ArrayAccess(keyArr, Expression.Constant(i++)),
                        Expression.Condition(needsKeyPrefix, Expression.Call(member, s_prepend, prefixValueAsArgs), member)));
                }
            }


            if (args.Count != 0)
            {
                // argArr = result.ArgsArray;
                operations.Add(Expression.Assign(argArr, Expression.PropertyOrField(result, nameof(ScriptParameters.ArgArray))));

                int i = 0;
                foreach (var arg in args)
                {
                    Expression rhs;
                    var member = GetMember(objTyped, arg);
                    if (member.Type == typeof(RedisValue))
                    {
                        // ... = objTyped.{member}
                        rhs = member; // pass-thru
                    }
                    else if (member.Type == typeof(RedisKey))
                    {
                        int keyIndex = keys.IndexOf(arg);
                        Debug.Assert(keyIndex >= 0);
                        // ... = keys[{index}].AsRedisValue()
                        rhs = Expression.Call(Expression.ArrayAccess(keyArr, Expression.Constant(keyIndex)), s_asRedisValue);
                    }
                    else
                    {
                        // ... = (SomeConversion)objTyped.{member}
                        var conversion = _conversionOperators[member.Type];
                        rhs = Expression.Call(conversion, member);
                    }
                    // argArr[i++] = ...
                    operations.Add(Expression.Assign(Expression.ArrayAccess(argArr, Expression.Constant(i++)), rhs));
                }
            }

            operations.Add(result); // final operation: return result
            var body = Expression.Lambda<Func<object, RedisKey?, ScriptParameters>>(
                Expression.Block(
                    typeof(ScriptParameters), // return type of the block
                    new ParameterExpression[] { objTyped, result, keyArr, argArr, needsPrefix, prefixValue }, // locals scoped by the block
                    operations), // the operations to perform
                objUntyped, keyPrefix); // parameters to the lambda
            return body.Compile();
        }
    }
}
