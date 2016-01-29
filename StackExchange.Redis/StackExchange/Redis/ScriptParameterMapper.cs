using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

namespace StackExchange.Redis
{
    class ScriptParameterMapper
    {
        public struct ScriptParameters
        {
            public RedisKey[] Keys;
            public RedisValue[] Arguments;

            public static readonly ConstructorInfo Cons = typeof(ScriptParameters).GetConstructor(new[] { typeof(RedisKey[]), typeof(RedisValue[]) });
            public ScriptParameters(RedisKey[] keys, RedisValue[] args)
            {
                Keys = keys;
                Arguments = args;
            }
        }

        static readonly Regex ParameterExtractor = new Regex(@"@(?<paramName> ([a-z]|_) ([a-z]|_|\d)*)", InternalRegexCompiledOption.Default | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
        static string[] ExtractParameters(string script)
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

        static string MakeOrdinalScriptWithoutKeys(string rawScript, string[] args)
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
                ret.Append(rawScript.Substring(upTo, ix - upTo));

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

            ret.Append(rawScript.Substring(upTo, rawScript.Length - upTo));

            return ret.ToString();
        }

        static void LoadMember(ILGenerator il, MemberInfo member)
        {
            // stack starts:
            // T(*?)

            var asField = member as FieldInfo;
            if (asField != null)
            {
                il.Emit(OpCodes.Ldfld, asField);        // typeof(member)
                return;
            }

            var asProp = member as PropertyInfo;
            if (asProp != null)
            {
                var getter = asProp.GetGetMethod();
                if (getter.IsVirtual)
                {
                    il.Emit(OpCodes.Callvirt, getter);  // typeof(member)
                }
                else
                {
                    il.Emit(OpCodes.Call, getter);      // typeof(member)
                }

                return;
            }

            throw new Exception("Should't be possible");
        }

        static readonly MethodInfo RedisValue_FromInt = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(int) });
        static readonly MethodInfo RedisValue_FromNullableInt = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(int?) });
        static readonly MethodInfo RedisValue_FromLong = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(long) });
        static readonly MethodInfo RedisValue_FromNullableLong = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(long?) });
        static readonly MethodInfo RedisValue_FromDouble= typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(double) });
        static readonly MethodInfo RedisValue_FromNullableDouble = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(double?) });
        static readonly MethodInfo RedisValue_FromString = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(string) });
        static readonly MethodInfo RedisValue_FromByteArray = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(byte[]) });
        static readonly MethodInfo RedisValue_FromBool = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(bool) });
        static readonly MethodInfo RedisValue_FromNullableBool = typeof(RedisValue).GetMethod("op_Implicit", new[] { typeof(bool?) });
        static readonly MethodInfo RedisKey_AsRedisValue = typeof(RedisKey).GetMethod("AsRedisValue", BindingFlags.NonPublic | BindingFlags.Instance);
        static void ConvertToRedisValue(MemberInfo member, ILGenerator il, LocalBuilder needsPrefixBool, ref LocalBuilder redisKeyLoc)
        {
            // stack starts:
            // typeof(member)

            var t = member is FieldInfo ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;

            if (t == typeof(RedisValue))
            {
                // They've already converted for us, don't do anything
                return;
            }

            if (t == typeof(RedisKey))
            {
                redisKeyLoc = redisKeyLoc ?? il.DeclareLocal(typeof(RedisKey));
                PrefixIfNeeded(il, needsPrefixBool, ref redisKeyLoc);   // RedisKey
                il.Emit(OpCodes.Stloc, redisKeyLoc);                    // --empty--
                il.Emit(OpCodes.Ldloca, redisKeyLoc);                   // RedisKey*
                il.Emit(OpCodes.Call, RedisKey_AsRedisValue);           // RedisValue
                return;
            }

            MethodInfo convertOp = null;
            if (t == typeof(int)) convertOp = RedisValue_FromInt;
            if (t == typeof(int?)) convertOp = RedisValue_FromNullableInt;
            if (t == typeof(long)) convertOp = RedisValue_FromLong;
            if (t == typeof(long?)) convertOp = RedisValue_FromNullableLong;
            if (t == typeof(double)) convertOp = RedisValue_FromDouble;
            if (t == typeof(double?)) convertOp = RedisValue_FromNullableDouble;
            if (t == typeof(string)) convertOp = RedisValue_FromString;
            if (t == typeof(byte[])) convertOp = RedisValue_FromByteArray;
            if (t == typeof(bool)) convertOp = RedisValue_FromBool;
            if (t == typeof(bool?)) convertOp = RedisValue_FromNullableBool;
            
            il.Emit(OpCodes.Call, convertOp);

            // stack ends:
            // RedisValue
        }

        /// <summary>
        /// Turns a script with @namedParameters into a LuaScript that can be executed
        /// against a given IDatabase(Async) object
        /// </summary>
        public static LuaScript PrepareScript(string script)
        {
            var ps = ExtractParameters(script);
            var ordinalScript = MakeOrdinalScriptWithoutKeys(script, ps);

            return new LuaScript(script, ordinalScript, ps);
        }

        static readonly HashSet<Type> ConvertableTypes =
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
                if(!ConvertableTypes.Contains(memberType)){
                    missingMember = null;
                    badTypeMember = argName;
                    return false;
                }
            }

            missingMember = badTypeMember = null;
            return true;
        }

        static void PrefixIfNeeded(ILGenerator il, LocalBuilder needsPrefixBool, ref LocalBuilder redisKeyLoc)
        {
            // top of stack is
            // RedisKey

            var getVal = typeof(RedisKey?).GetProperty("Value").GetGetMethod();
            var prepend = typeof(RedisKey).GetMethod("Prepend");

            var doNothing = il.DefineLabel();
            redisKeyLoc = redisKeyLoc ?? il.DeclareLocal(typeof(RedisKey));

            il.Emit(OpCodes.Ldloc, needsPrefixBool);    // RedisKey bool
            il.Emit(OpCodes.Brfalse, doNothing);        // RedisKey
            il.Emit(OpCodes.Stloc, redisKeyLoc);        // --empty--
            il.Emit(OpCodes.Ldloca, redisKeyLoc);       // RedisKey*
            il.Emit(OpCodes.Ldarga_S, 1);               // RedisKey* RedisKey?*
            il.Emit(OpCodes.Call, getVal);              // RedisKey* RedisKey
            il.Emit(OpCodes.Call, prepend);             // RedisKey


            il.MarkLabel(doNothing);                    // RedisKey
        }

        /// <summary>
        /// Creates a Func that extracts parameters from the given type for use by a LuaScript.
        /// 
        /// Members that are RedisKey's get extracted to be passed in as keys to redis; all members that
        /// appear in the script get extracted as RedisValue arguments to be sent up as args.
        /// 
        /// We send all values as arguments so we don't have to prepare the same script for different parameter
        /// types.
        /// 
        /// The created Func takes a RedisKey, which will be prefixed to all keys (and arguments of type RedisKey) for 
        /// keyspace isolation.
        /// </summary>
        public static Func<object, RedisKey?, ScriptParameters> GetParameterExtractor(Type t, LuaScript script)
        {
            string ignored;
            if (!IsValidParameterHash(t, script, out ignored, out ignored)) throw new Exception("Shouldn't be possible");

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

                args.Add(member);
            }

            var nullableRedisKeyHasValue = typeof(RedisKey?).GetProperty("HasValue").GetGetMethod();

            var dyn = new DynamicMethod("ParameterExtractor_" + t.FullName + "_" + script.OriginalScript.GetHashCode(), typeof(ScriptParameters), new[] { typeof(object), typeof(RedisKey?) }, restrictedSkipVisibility: true);
            var il = dyn.GetILGenerator();

            // only init'd if we use it
            LocalBuilder redisKeyLoc = null;
            var loc = il.DeclareLocal(t);
            il.Emit(OpCodes.Ldarg_0);               // object
#if !CORE_CLR
            if (t.IsValueType)
#else
            if (t.GetTypeInfo().IsValueType)
#endif
            {
                il.Emit(OpCodes.Unbox_Any, t);      // T
            }
            else
            {
                il.Emit(OpCodes.Castclass, t);      // T
            }
            il.Emit(OpCodes.Stloc, loc);            // --empty--

            var needsKeyPrefixLoc = il.DeclareLocal(typeof(bool));
            
            il.Emit(OpCodes.Ldarga_S, 1);                       // RedisKey?*
            il.Emit(OpCodes.Call, nullableRedisKeyHasValue);    // bool
            il.Emit(OpCodes.Stloc, needsKeyPrefixLoc);          // --empty--

            if (keys.Count == 0)
            {
                // if there are no keys, don't allocate
                il.Emit(OpCodes.Ldnull);                    // null
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, keys.Count);        // int
                il.Emit(OpCodes.Newarr, typeof(RedisKey));  // RedisKey[]
            }

            for (var i = 0; i < keys.Count; i++)
            {
                il.Emit(OpCodes.Dup);                       // RedisKey[] RedisKey[]
                il.Emit(OpCodes.Ldc_I4, i);                 // RedisKey[] RedisKey[] int
#if !CORE_CLR
                if (t.IsValueType)
#else
                if (t.GetTypeInfo().IsValueType)
#endif
                {
                    il.Emit(OpCodes.Ldloca, loc);           // RedisKey[] RedisKey[] int T*
                }
                else
                {
                    il.Emit(OpCodes.Ldloc, loc);            // RedisKey[] RedisKey[] int T
                }
                LoadMember(il, keys[i]);                                // RedisKey[] RedisKey[] int RedisKey
                PrefixIfNeeded(il, needsKeyPrefixLoc, ref redisKeyLoc); // RedisKey[] RedisKey[] int RedisKey
                il.Emit(OpCodes.Stelem, typeof(RedisKey));              // RedisKey[]
            }

            if (args.Count == 0)
            {
                // if there are no args, don't allocate
                il.Emit(OpCodes.Ldnull);                        // RedisKey[] null
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, args.Count);            // RedisKey[] int
                il.Emit(OpCodes.Newarr, typeof(RedisValue));    // RedisKey[] RedisValue[]
            }

            for (var i = 0; i < args.Count; i++)
            {
                il.Emit(OpCodes.Dup);                       // RedisKey[] RedisValue[] RedisValue[]
                il.Emit(OpCodes.Ldc_I4, i);                 // RedisKey[] RedisValue[] RedisValue[] int
#if !CORE_CLR
                if (t.IsValueType)
#else
                if (t.GetTypeInfo().IsValueType)
#endif
                {
                    il.Emit(OpCodes.Ldloca, loc);           // RedisKey[] RedisValue[] RedisValue[] int T*
                }
                else
                {
                    il.Emit(OpCodes.Ldloc, loc);            // RedisKey[] RedisValue[] RedisValue[] int T
                }
                
                var member = args[i];
                LoadMember(il, member);                                                 // RedisKey[] RedisValue[] RedisValue[] int memberType
                ConvertToRedisValue(member, il, needsKeyPrefixLoc, ref redisKeyLoc);   // RedisKey[] RedisValue[] RedisValue[] int RedisValue

                il.Emit(OpCodes.Stelem, typeof(RedisValue));        // RedisKey[] RedisValue[]
            }

            il.Emit(OpCodes.Newobj, ScriptParameters.Cons); // ScriptParameters
            il.Emit(OpCodes.Ret);                           // --empty--

            var ret = (Func<object, RedisKey?, ScriptParameters>)dyn.CreateDelegate(typeof(Func<object, RedisKey?, ScriptParameters>));

            return ret;
        }
    }
}
