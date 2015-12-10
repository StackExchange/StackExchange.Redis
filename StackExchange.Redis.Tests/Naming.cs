using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class Naming
    {
        [Test]
        [TestCase(typeof(IDatabase), false)]
        [TestCase(typeof(IDatabaseAsync), true)]
        [TestCase(typeof(Condition), false)]
        public void CheckSignatures(Type type, bool isAsync)
        {
            // check that all methods and interfaces look appropriate for their sync/async nature
            CheckName(type.GetTypeInfo(), isAsync);
            var members = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach(var member in members)
            {
                if (member.Name.StartsWith("get_") || member.Name.StartsWith("set_") || member.Name.StartsWith("add_") || member.Name.StartsWith("remove_")) continue;
                CheckMethod(member, isAsync);
            }
        }

        [Test]
        public void ShowReadOnlyOperations()
        {
            var msg = typeof(ConnectionMultiplexer).GetTypeInfo().Assembly.GetType("StackExchange.Redis.Message");
            Assert.IsNotNull(msg, "Message");
            var cmd = typeof(ConnectionMultiplexer).GetTypeInfo().Assembly.GetType("StackExchange.Redis.RedisCommand");
            Assert.IsNotNull(cmd, "RedisCommand");
            var method = msg.GetMethod("IsMasterOnly", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(method, "IsMasterOnly");
            object[] args = new object[1];

            List<object> masterSlave = new List<object>();
            List<object> masterOnly = new List<object>();
            foreach (var val in Enum.GetValues(cmd))
            {
                args[0] = val;
                bool isMasterOnly = (bool)method.Invoke(null, args);
                (isMasterOnly ? masterOnly : masterSlave).Add(val);
                
                if(!isMasterOnly)
                {
                    Console.WriteLine(val);
                }
            }
            Console.WriteLine("master-only: {0}, vs master/slave: {1}", masterOnly.Count, masterSlave.Count);
            Console.WriteLine();
            Console.WriteLine("master-only:");
            foreach (var val in masterOnly) Console.WriteLine(val);
            Console.WriteLine();
            Console.WriteLine("master/slave:");
            foreach (var val in masterSlave) Console.WriteLine(val);
        }

        [Test]
        [TestCase(typeof(IDatabase))]
        [TestCase(typeof(IDatabaseAsync))]
        public void CheckDatabaseMethodsUseKeys(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IgnoreMethodConventions(method)) continue;

                switch(method.Name)
                {
                    case "KeyRandom":
                    case "KeyRandomAsync":
                    case "Publish":
                    case "PublishAsync":
                        continue; // they're fine, but don't want to widen check to return type
                }

                bool usesKey = method.GetParameters().Any(p => UsesKey(p.ParameterType));
                Assert.IsTrue(usesKey, type.Name + ":" + method.Name);
            }
        }
        static bool UsesKey(Type type)
        {
            if (type == typeof(RedisKey)) return true;

            if(type.IsArray)
            {
                if (UsesKey(type.GetElementType())) return true;
            }
            if(type.GetTypeInfo().IsGenericType) // KVP, etc
            {
                var args = type.GetGenericArguments();
                if (args.Any(UsesKey)) return true;
            }
            return false;
        }

        static bool IgnoreMethodConventions(MethodInfo method)
        {
            string name = method.Name;
            if (name.StartsWith("get_") || name.StartsWith("set_") || name.StartsWith("add_") || name.StartsWith("remove_")) return true;
            switch(name)
            {
                case "CreateBatch":
                case "CreateTransaction":
                case "IsConnected":
                case "SetScan":
                case "SortedSetScan":
                case "HashScan":
                case "SubscribedEndpoint":
                    return true;
            }
            return false;
        }
        [Test]
        [TestCase(typeof(IDatabase), typeof(IDatabaseAsync))]
        [TestCase(typeof(IDatabaseAsync), typeof(IDatabase))]
        public void CheckSyncAsyncMethodsMatch(Type from, Type to)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            int count = 0;
            foreach (var method in from.GetMethods(flags))
            {
                if (IgnoreMethodConventions(method)) continue;

                string name = method.Name, huntName;

                if (name.EndsWith("Async")) huntName = name.Substring(0, name.Length - 5);
                else huntName = name + "Async";

                Type huntType;
                if (method.ReturnType == null || method.ReturnType == typeof(void))
                {
                    huntType = typeof(Task);
                }
                else if (method.ReturnType == typeof(Task))
                {
                    huntType = null;
                }
                else if (method.ReturnType.GetTypeInfo().IsSubclassOf(typeof(Task)))
                {
                    huntType = method.ReturnType.GetGenericArguments()[0];
                }
                else
                {
                    huntType = typeof(Task<>).MakeGenericType(method.ReturnType);
                }
                var pFrom = method.GetParameters();
                Type[] args = pFrom.Select(x => x.ParameterType).ToArray();
                Assert.AreEqual(typeof(CommandFlags), args.Last());
#if !CORE_CLR
                var found = to.GetMethod(huntName, flags, null, method.CallingConvention, args, null);
#else
                var found = to.GetMethods(flags)
                    .SingleOrDefault(m => m.Name == huntName && m.HasMatchingParameterTypes(args));
#endif
                Assert.IsNotNull(found, "Found " + name + ", no " + huntName);
                var pTo = found.GetParameters();

                for(int i = 0; i < pFrom.Length;i++)
                {
                    Assert.AreEqual(pFrom[i].Name, pTo[i].Name, method.Name + ":" + pFrom[i].Name);
                    Assert.AreEqual(pFrom[i].ParameterType, pTo[i].ParameterType, method.Name + ":" + pFrom[i].Name);
                }

                
                count++;
            }
            Console.WriteLine("Validated: {0} ({1} methods)", from.Name, count);
        }

        static readonly Type ignoreType = typeof(ConnectionMultiplexer).GetTypeInfo().Assembly.GetType("StackExchange.Redis.IgnoreNamePrefixAttribute");
        void CheckMethod(MethodInfo method, bool isAsync)
        {

#if DEBUG
#if !CORE_CLR
            bool ignorePrefix = ignoreType != null && Attribute.IsDefined(method, ignoreType);
#else
            bool ignorePrefix = ignoreType != null && method.IsDefined(ignoreType);
#endif
            if (ignorePrefix)
            {
#if !CORE_CLR
                Attribute attrib = Attribute.GetCustomAttribute(method, ignoreType);
#else
                Attribute attrib = method.GetCustomAttribute(ignoreType);
#endif
                if ((bool)attrib.GetType().GetProperty("IgnoreEntireMethod").GetValue(attrib))
                {
                    return;
                }
            }
            string shortName = method.Name, fullName = method.DeclaringType.Name + "." + shortName;
            CheckName(method, isAsync);
            if (!ignorePrefix)
            {   
                Assert.IsTrue(shortName.StartsWith("Hash") || shortName.StartsWith("Key")
                    || shortName.StartsWith("String") || shortName.StartsWith("List")
                    || shortName.StartsWith("SortedSet") || shortName.StartsWith("Set")
                    || shortName.StartsWith("Debug") || shortName.StartsWith("Lock")
                    || shortName.StartsWith("Script") || shortName.StartsWith("HyperLogLog")
                    , fullName + ":Prefix");
            }

            Assert.IsFalse(shortName.Contains("If"), fullName + ":If"); // should probably be a When option

            var returnType = method.ReturnType ?? typeof(void);
            if (isAsync)
            {
                Assert.IsTrue(typeof(Task).IsAssignableFrom(returnType), fullName + ":Task");
            }
            else
            {
                Assert.IsFalse(typeof(Task).IsAssignableFrom(returnType), fullName + ":Task");
            }
#endif
        }

        void CheckName(MemberInfo member, bool isAsync)
        {
            if (isAsync) Assert.IsTrue(member.Name.EndsWith("Async"), member.Name + ":Name - end *Async");
            else Assert.IsFalse(member.Name.EndsWith("Async"), member.Name + ":Name - don't end *Async");
        }
    }

    public static class ReflectionExtensions
    {
#if !CORE_CLR
        public static Type GetTypeInfo(this Type type)
        {
            return type;
        }

#else
        public static bool HasMatchingParameterTypes(this MethodInfo method, Type[] paramTypes)
        {
            var types = method.GetParameters().Select(pi => pi.ParameterType).ToArray();
            if (types.Length != paramTypes.Length)
            {
                return false;
            }

            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] != paramTypes[i])
                {
                    return false;
                }
            }

            return true;
        }
#endif
    }
}
