﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Naming
    {
        public ITestOutputHelper Output;
        public Naming(ITestOutputHelper output) => Output = output;

        [Theory]
        [InlineData(typeof(IDatabase), false)]
        [InlineData(typeof(IDatabaseAsync), true)]
        [InlineData(typeof(Condition), false)]
        public void CheckSignatures(Type type, bool isAsync)
        {
            // check that all methods and interfaces look appropriate for their sync/async nature
            CheckName(type.GetTypeInfo(), isAsync);
            var members = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var member in members)
            {
                if (member.Name.StartsWith("get_") || member.Name.StartsWith("set_") || member.Name.StartsWith("add_") || member.Name.StartsWith("remove_")) continue;
                CheckMethod(member, isAsync);
            }
        }

        [Fact]
        public void ShowReadOnlyOperations()
        {
            var msg = typeof(ConnectionMultiplexer).GetTypeInfo().Assembly.GetType("StackExchange.Redis.Message");
            Assert.NotNull(msg);
            var cmd = typeof(ConnectionMultiplexer).GetTypeInfo().Assembly.GetType("StackExchange.Redis.RedisCommand");
            Assert.NotNull(cmd);
            var masterOnlyMethod = msg.GetMethod(nameof(Message.IsMasterOnly), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(masterOnlyMethod);
            object[] args = new object[1];

            List<object> masterSlave = new List<object>();
            List<object> masterOnly = new List<object>();
            foreach (var val in Enum.GetValues(cmd))
            {
                args[0] = val;
                bool isMasterOnly = (bool)masterOnlyMethod.Invoke(null, args);
                (isMasterOnly ? masterOnly : masterSlave).Add(val);

                if (!isMasterOnly)
                {
                    Output.WriteLine(val?.ToString());
                }
            }
            Output.WriteLine("master-only: {0}, vs master/slave: {1}", masterOnly.Count, masterSlave.Count);
            Output.WriteLine("");
            Output.WriteLine("master-only:");
            foreach (var val in masterOnly)
            {
                Output.WriteLine(val?.ToString());
            }
            Output.WriteLine("");
            Output.WriteLine("master/slave:");
            foreach (var val in masterSlave)
            {
                Output.WriteLine(val?.ToString());
            }
        }

        [Theory]
        [InlineData(typeof(IDatabase))]
        [InlineData(typeof(IDatabaseAsync))]
        public void CheckDatabaseMethodsUseKeys(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IgnoreMethodConventions(method)) continue;

                switch (method.Name)
                {
                    case nameof(IDatabase.KeyRandom):
                    case nameof(IDatabaseAsync.KeyRandomAsync):
                    case nameof(IDatabase.Publish):
                    case nameof(IDatabaseAsync.PublishAsync):
                    case nameof(IDatabase.Execute):
                    case nameof(IDatabaseAsync.ExecuteAsync):
                    case nameof(IDatabase.ScriptEvaluate):
                    case nameof(IDatabaseAsync.ScriptEvaluateAsync):
                        continue; // they're fine, but don't want to widen check to return type
                }

                bool usesKey = method.GetParameters().Any(p => UsesKey(p.ParameterType));
                Assert.True(usesKey, type.Name + ":" + method.Name);
            }
        }

        private static bool UsesKey(Type type)
        {
            if (type == typeof(RedisKey)) return true;

            if (type.IsArray)
            {
                if (UsesKey(type.GetElementType())) return true;
            }
            if (type.GetTypeInfo().IsGenericType) // KVP, etc
            {
                var args = type.GetGenericArguments();
                if (args.Any(UsesKey)) return true;
            }
            return false;
        }

        private static bool IgnoreMethodConventions(MethodInfo method)
        {
            string name = method.Name;
            if (name.StartsWith("get_") || name.StartsWith("set_") || name.StartsWith("add_") || name.StartsWith("remove_")) return true;
            switch (name)
            {
                case nameof(IDatabase.CreateBatch):
                case nameof(IDatabase.CreateTransaction):
                case nameof(IDatabase.Execute):
                case nameof(IDatabaseAsync.ExecuteAsync):
                case nameof(IDatabase.IsConnected):
                case nameof(IDatabase.SetScan):
                case nameof(IDatabase.SortedSetScan):
                case nameof(IDatabase.HashScan):
                case nameof(ISubscriber.SubscribedEndpoint):
                    return true;
            }
            return false;
        }

        [Theory]
        [InlineData(typeof(IDatabase), typeof(IDatabaseAsync))]
        [InlineData(typeof(IDatabaseAsync), typeof(IDatabase))]
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
                Output.WriteLine("Checking: {0}.{1}", from.Name, method.Name);
                Assert.Equal(typeof(CommandFlags), args.Last());
#if !NETCOREAPP1_0
                var found = to.GetMethod(huntName, flags, null, method.CallingConvention, args, null);
#else
                var found = to.GetMethods(flags)
                    .SingleOrDefault(m => m.Name == huntName && m.HasMatchingParameterTypes(args));
#endif
                Assert.NotNull(found); // "Found " + name + ", no " + huntName
                var pTo = found.GetParameters();

                for (int i = 0; i < pFrom.Length; i++)
                {
                    Assert.Equal(pFrom[i].Name, pTo[i].Name); // method.Name + ":" + pFrom[i].Name
                    Assert.Equal(pFrom[i].ParameterType, pTo[i].ParameterType); // method.Name + ":" + pFrom[i].Name
                }

                count++;
            }
            Output.WriteLine("Validated: {0} ({1} methods)", from.Name, count);
        }

        private static readonly Type ignoreType = typeof(ConnectionMultiplexer).GetTypeInfo().Assembly.GetType("StackExchange.Redis.IgnoreNamePrefixAttribute");
        private void CheckMethod(MethodInfo method, bool isAsync)
        {
#if DEBUG
#if !NETCOREAPP1_0
            bool ignorePrefix = ignoreType != null && Attribute.IsDefined(method, ignoreType);
#else
            bool ignorePrefix = ignoreType != null && method.IsDefined(ignoreType);
#endif
            if (ignorePrefix)
            {
#if !NETCOREAPP1_0
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
                Assert.True(
                    shortName.StartsWith("Debug")
                    || shortName.StartsWith("Execute")
                    || shortName.StartsWith("Geo")
                    || shortName.StartsWith("Hash")
                    || shortName.StartsWith("HyperLogLog")
                    || shortName.StartsWith("Key")
                    || shortName.StartsWith("List")
                    || shortName.StartsWith("Lock")
                    || shortName.StartsWith("Publish")
                    || shortName.StartsWith("Set")
                    || shortName.StartsWith("Script")
                    || shortName.StartsWith("SortedSet")
                    || shortName.StartsWith("String") 
                    , fullName + ":Prefix");
            }

            Assert.False(shortName.Contains("If"), fullName + ":If"); // should probably be a When option

            var returnType = method.ReturnType ?? typeof(void);
            if (isAsync)
            {
                Assert.True(typeof(Task).IsAssignableFrom(returnType), fullName + ":Task");
            }
            else
            {
                Assert.False(typeof(Task).IsAssignableFrom(returnType), fullName + ":Task");
            }
#endif
        }

        private void CheckName(MemberInfo member, bool isAsync)
        {
            if (isAsync) Assert.True(member.Name.EndsWith("Async"), member.Name + ":Name - end *Async");
            else Assert.False(member.Name.EndsWith("Async"), member.Name + ":Name - don't end *Async");
        }
    }

    public static class ReflectionExtensions
    {
#if !NETCOREAPP1_0
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
