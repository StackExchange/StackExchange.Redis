using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public class NamingTests : TestBase
{
    public NamingTests(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData(typeof(IDatabase), false)]
    [InlineData(typeof(IDatabaseAsync), true)]
    [InlineData(typeof(Condition), false)]
    public void CheckSignatures(Type type, bool isAsync)
    {
        // check that all methods and interfaces look appropriate for their sync/async nature
        CheckName(type, isAsync);
        var members = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var member in members)
        {
            if (member.Name.StartsWith("get_") || member.Name.StartsWith("set_") || member.Name.StartsWith("add_") || member.Name.StartsWith("remove_")) continue;
            CheckMethod(member, isAsync);
        }
    }

    /// <summary>
    /// This test iterates over all <see cref="RedisCommand"/>s to ensure we have everything accounted for as primary-only or not.
    /// </summary>
    [Fact]
    public void CheckReadOnlyOperations()
    {
        List<RedisCommand> primaryReplica = new(),
                           primaryOnly = new();
        foreach (var val in (RedisCommand[])Enum.GetValues(typeof(RedisCommand)))
        {
            bool isPrimaryOnly = val.IsPrimaryOnly();
            (isPrimaryOnly ? primaryOnly : primaryReplica).Add(val);

            if (!isPrimaryOnly)
            {
                Log(val.ToString());
            }
        }
        // Ensure an unknown command from nowhere would violate the check above, as any not-yet-added one would.
        Assert.Throws<ArgumentOutOfRangeException>(() => ((RedisCommand)99999).IsPrimaryOnly());

        Log("primary-only: {0}, vs primary/replica: {1}", primaryOnly.Count, primaryReplica.Count);
        Log("");
        Log("primary-only:");
        foreach (var val in primaryOnly)
        {
            Log(val.ToString());
        }
        Log("");
        Log("primary/replica:");
        foreach (var val in primaryReplica)
        {
            Log(val.ToString());
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
                case nameof(IDatabase.StreamRead):
                case nameof(IDatabase.StreamReadAsync):
                case nameof(IDatabase.StreamReadGroup):
                case nameof(IDatabase.StreamReadGroupAsync):
                    continue; // they're fine, but don't want to widen check to return type
            }

            bool usesKey = method.GetParameters().Any(p => UsesKey(p.ParameterType));
            Assert.True(usesKey, type.Name + ":" + method.Name);
        }
    }

    private static bool UsesKey(Type type) =>
        type == typeof(RedisKey)
        || (type.IsArray && UsesKey(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(UsesKey));

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
            var pFrom = method.GetParameters();
            Type[] args = pFrom.Select(x => x.ParameterType).ToArray();
            Log("Checking: {0}.{1}", from.Name, method.Name);
            if (method.GetCustomAttribute<EditorBrowsableAttribute>() is EditorBrowsableAttribute attr && attr.State == EditorBrowsableState.Never)
            {
                // For compatibility overloads, explicitly don't ensure CommandFlags is last
            }
            else
            {
                Assert.Equal(typeof(CommandFlags), args.Last());
            }
            var found = to.GetMethod(huntName, flags, null, method.CallingConvention, args, null);
            Assert.NotNull(found); // "Found " + name + ", no " + huntName
            var pTo = found.GetParameters();

            for (int i = 0; i < pFrom.Length; i++)
            {
                Assert.Equal(pFrom[i].Name, pTo[i].Name); // method.Name + ":" + pFrom[i].Name
                Assert.Equal(pFrom[i].ParameterType, pTo[i].ParameterType); // method.Name + ":" + pFrom[i].Name
            }

            count++;
        }
        Log("Validated: {0} ({1} methods)", from.Name, count);
    }

    private void CheckMethod(MethodInfo method, bool isAsync)
    {
        string shortName = method.Name, fullName = method.DeclaringType?.Name + "." + shortName;

        switch (shortName)
        {
            case nameof(IDatabaseAsync.IsConnected):
                return;
            case nameof(IDatabase.CreateBatch):
            case nameof(IDatabase.CreateTransaction):
            case nameof(IDatabase.IdentifyEndpoint):
            case nameof(IDatabase.Sort):
            case nameof(IDatabase.SortAndStore):
            case nameof(IDatabaseAsync.IdentifyEndpointAsync):
            case nameof(IDatabaseAsync.SortAsync):
            case nameof(IDatabaseAsync.SortAndStoreAsync):
                CheckName(method, isAsync);
                break;
            default:
                CheckName(method, isAsync);
                var isValid = shortName.StartsWith("Debug")
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
                    || shortName.StartsWith("Stream");
                Log(fullName + ": " + (isValid ? "valid" : "invalid"));
                Assert.True(isValid, fullName + ":Prefix");
                break;
        }

        Assert.False(shortName.Contains("If"), fullName + ":If"); // should probably be a When option

        var returnType = method.ReturnType ?? typeof(void);

        if (isAsync)
        {
            Assert.True(IsAsyncMethod(returnType), fullName + ":Task");
        }
        else
        {
            Assert.False(IsAsyncMethod(returnType), fullName + ":Task");
        }

        static bool IsAsyncMethod(Type returnType)
        {
            if (returnType == typeof(Task)) return true;
            if (returnType == typeof(ValueTask)) return true;

            if (returnType.IsGenericType)
            {
                var genDef = returnType.GetGenericTypeDefinition();
                if (genDef == typeof(Task<>)) return true;
                if (genDef == typeof(ValueTask<>)) return true;
                if (genDef == typeof(IAsyncEnumerable<>)) return true;
            }

            return false;
        }
    }

    private static void CheckName(MemberInfo member, bool isAsync)
    {
        if (isAsync) Assert.True(member.Name.EndsWith("Async"), member.Name + ":Name - end *Async");
        else Assert.False(member.Name.EndsWith("Async"), member.Name + ":Name - don't end *Async");
    }
}
