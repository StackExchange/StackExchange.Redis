using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// readable names for the info we gather; kept as tuples (+ BasicArray) so they have
// value equality and play nicely with incremental generator caching.
using ParamInfo = (string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default);
using MethodInfo = (string Name, string ReturnType, StackExchange.Redis.Build.BasicArray<(string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default)> Parameters, StackExchange.Redis.Build.BasicArray<string> TypeArgs);
using InterfaceInfo = (string Name, string Namespace, StackExchange.Redis.Build.AutoDatabaseGenerator.KnownInterfaces KnownType, StackExchange.Redis.Build.BasicArray<(string Name, string ReturnType, StackExchange.Redis.Build.BasicArray<(string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default)> Parameters, StackExchange.Redis.Build.BasicArray<string> TypeArgs)> Methods);
using ClassInfo = (string Name, string Namespace, StackExchange.Redis.Build.AutoDatabaseGenerator.KnownInterfaces Interfaces, bool IsMutator);

namespace StackExchange.Redis.Build;

[Generator(LanguageNames.CSharp)]
public class AutoDatabaseGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        var interfaces = ctx.SyntaxProvider
             .CreateSyntaxProvider(
                 static (node, _) => node is InterfaceDeclarationSyntax decl && FastIndexFilter(decl), ExtractInterfaceMethods)
             .Where(pair => pair.Name is { Length: > 0 })
             .Collect();

        var classes = ctx.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax decl && FastClassFilter(decl), ExtractClasses)
            .Where(pair => pair.Name is { Length: > 0 })
            .Collect();

        ctx.RegisterSourceOutput(interfaces.Combine(classes), static (ctx, content) => Generate(ctx, content.Left, content.Right));
    }

    /// <summary>
    /// The only assembly this generator has anything to say about.
    /// </summary>
    /// <remarks>
    /// This is repo-internal machinery, but it now ships as an analyzer inside the StackExchange.Redis package
    /// (for <c>AsciiHashGenerator</c>'s benefit), so it is loaded by every consumer. It can never generate
    /// anything useful for them - the semantic checks below reject anything that isn't our own
    /// <c>StackExchange.Redis</c> declaration - so short-circuit on the assembly name first, and skip even the
    /// semantic-model query for the consumers who happen to declare a type called <c>IDatabase</c>.
    /// </remarks>
    private const string OwningAssembly = "StackExchange.Redis";

    static KnownInterfaces Identify(string type) => type switch
    {
        "IDatabase" => KnownInterfaces.IDatabase,
        "IDatabaseAsync" => KnownInterfaces.IDatabaseAsync,
        "IRedis" => KnownInterfaces.IRedis,
        "IRedisAsync" => KnownInterfaces.IRedisAsync,
        _ => KnownInterfaces.None,
    };

    private static bool FastIndexFilter(InterfaceDeclarationSyntax decl) // limit to IDatabase, IDatabaseAsync
        => Identify(decl.Identifier.ValueText) is not 0;

    private static bool FastClassFilter(ClassDeclarationSyntax decl) // limit to IDatabase, IDatabaseAsync
        => decl.AttributeLists.Any(x => x.Attributes.Any(x => x.Name.ToString() is "AutoDatabase" or "AutoDatabaseAttribute"));

    // does this auto-database rewrite keys/channels? if not, the captured-args structs need no mapping members
    private static bool IsMutatorInterface(INamedTypeSymbol symbol) =>
        symbol is
        {
            TypeKind: TypeKind.Interface,
            Name: "IRedisArgsMutator",
            ContainingType: null,
            ContainingNamespace:
            {
                Name: "Redis",
                ContainingNamespace:
                {
                    Name: "StackExchange",
                    ContainingNamespace.IsGlobalNamespace: true
                }
            }
        };

    private static bool IsOurInterface(INamedTypeSymbol symbol) =>
        symbol is
        {
            TypeKind: TypeKind.Interface,
            Name: "IDatabase" or "IDatabaseAsync" or "IRedis" or "IRedisAsync",
            ContainingType: null,
            ContainingNamespace:
            {
                Name: "Redis",
                ContainingNamespace:
                {
                    Name: "StackExchange",
                    ContainingNamespace.IsGlobalNamespace: true
                }
            }
        };

    private static InterfaceInfo ExtractInterfaceMethods(GeneratorSyntaxContext context, CancellationToken cancel)
    {
        if (context.SemanticModel.Compilation.AssemblyName is not OwningAssembly) return default;

        // note: we deliberately do NOT interpret anything here - just capture the raw shape of every
        // method (name, return type, and per-parameter name/type/modifiers/optionality/default) so that
        // later passes have everything they might need.
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancel) is not INamedTypeSymbol iface
            || !IsOurInterface(iface))
        {
            return default;
        }

        var knownType = Identify(iface.Name);
        if (knownType is KnownInterfaces.None) return default;

        var methods = new List<MethodInfo>();
        foreach (var member in iface.GetMembers())
        {
            cancel.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method) continue;

            var parameters = BasicArray<ParamInfo>.From(method.Parameters, static p => (
                Name: p.Name,
                Type: p.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                RefKind: p.RefKind,
                IsParams: p.IsParams,
                IsOptional: p.IsOptional,
                HasDefault: p.HasExplicitDefaultValue,
                Default: p.HasExplicitDefaultValue ? FormatDefault(p.ExplicitDefaultValue) : null));

            BasicArray<string> typeArgs = default;
            if (method.IsGenericMethod)
            {
                typeArgs = BasicArray<string>.From(method.TypeParameters, p => p.Name);
            }
            methods.Add((
                Name: method.Name,
                ReturnType: method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                Parameters: parameters,
                TypeArgs: typeArgs));
        }

        var ns = iface.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return (iface.Name, ns, knownType, BasicArray<MethodInfo>.From(methods));
    }

    private ClassInfo ExtractClasses(GeneratorSyntaxContext context, CancellationToken cancel)
    {
        if (context.SemanticModel.Compilation.AssemblyName is not OwningAssembly) return default;

        // note: we deliberately do NOT interpret anything here - just capture the raw shape of every
        // method (name, return type, and per-parameter name/type/modifiers/optionality/default) so that
        // later passes have everything they might need.
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancel) is not INamedTypeSymbol cls
            || !HasAutoDatabaseAttrib(cls))
        {
            return default;
        }

        static bool HasAutoDatabaseAttrib(INamedTypeSymbol symbol)
        {
            var attribs = symbol.GetAttributes();
            foreach (var attrib in attribs)
            {
                if (attrib.AttributeClass is
                    {
                        Name: "AutoDatabaseAttribute",
                        ContainingType: null,
                        ContainingNamespace:
                        {
                            Name: "Redis",
                            ContainingNamespace:
                            {
                                Name: "StackExchange",
                                ContainingNamespace.IsGlobalNamespace: true
                            }
                        }
                    })
                {
                    return true;
                }
            }

            return false;
        }

        KnownInterfaces known = 0;
        foreach (var iFace in cls.Interfaces)
        {
            if (IsOurInterface(iFace))
            {
                switch (iFace.Name)
                {
                    case "IDatabase":
                        known |= KnownInterfaces.IDatabase | KnownInterfaces.IDatabaseAsync |
                                 KnownInterfaces.IRedis | KnownInterfaces.IRedisAsync;
                        break;
                    case "IDatabaseAsync":
                        known |= KnownInterfaces.IDatabaseAsync | KnownInterfaces.IRedisAsync;
                        break;
                    case "IRedis":
                        known |= KnownInterfaces.IRedis | KnownInterfaces.IRedisAsync;
                        break;
                    case "IRedisAsync":
                        known |= KnownInterfaces.IRedisAsync;
                        break;
                }
            }
        }

        if (known is KnownInterfaces.None) return default; // nothing to do!

        // AllInterfaces (not Interfaces) so an inherited/explicit implementation still counts
        bool isMutator = false;
        foreach (var iFace in cls.AllInterfaces)
        {
            if (IsMutatorInterface(iFace))
            {
                isMutator = true;
                break;
            }
        }

        var ns = cls.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return (cls.Name, ns, known, isMutator);
    }

    [Flags]
    internal enum KnownInterfaces
    {
        None = 0,
        IDatabase = 1,
        IDatabaseAsync = 2,
        IRedis = 4,
        IRedisAsync = 8,
    }

    // methods that don't fit the capture-and-replay shape are left for the caller to implement manually:
    //  - generic methods (open type args can't be captured into a concrete state struct)
    //  - the Wait family (synchronization over caller-supplied Tasks, not server calls)
    //  - IsConnected: a synchronous status probe that IDatabaseAsync carries; it would route through the
    //    sync Execute funnel, which an async-only database (e.g. RetryDatabase) doesn't provide - and a
    //    connection check shouldn't be retried in any case
    //  - IdentifyEndpoint / IdentifyEndpointAsync: a routing lookup ("which endpoint serves this key"),
    //    not a replayable server command; wrappers want their own behaviour here (e.g. a connection-group
    //    resolving against the currently-active member and returning null when there is none)
    //  - streaming returns (IEnumerable<T> / IAsyncEnumerable<T>) whose execution is deferred
    private static bool SkipMethod(MethodInfo method)
        => !method.TypeArgs.IsEmpty
        || method.Name.Contains("Wait")
        || method.Name == "IsConnected"
        || method.Name.Contains("IdentifyEndpoint")
        // transaction/batch factories are not server round-trips and cannot be captured-and-replayed;
        // the (few) databases that offer them implement them by hand
        || method.Name == "CreateTransaction"
        || method.Name == "CreateBatch"
        || method.ReturnType.StartsWith("System.Collections.Generic.IEnumerable<", StringComparison.Ordinal)
        || method.ReturnType.StartsWith("System.Collections.Generic.IAsyncEnumerable<", StringComparison.Ordinal);

    private const string TaskType = "System.Threading.Tasks.Task";

    // Task / Task<T> returns route through ExecuteAsync (its own retry policy); everything else
    // uses Execute.
    private static bool IsAsync(string returnType)
        => returnType.StartsWith(TaskType, StringComparison.Ordinal);

    // the retry machinery unwraps the Task and only ever sees the inner result, so key/channel
    // (un)mapping must be decided on T, not Task<T>; this strips the Task<...> wrapper from an async
    // return type. A bare (non-generic) Task has no result and is returned unchanged (as is any
    // non-async return type), which is harmless since neither matches NeedsMap.
    private static string StripTask(string returnType)
        => IsAsync(returnType) && returnType.StartsWith(TaskType + "<", StringComparison.Ordinal)
            ? returnType.Substring(TaskType.Length + 1, returnType.Length - TaskType.Length - 2)
            : returnType;

    private static string FormatDefault(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null",
    };

    private static void Generate(SourceProductionContext ctx, ImmutableArray<InterfaceInfo> interfaces, ImmutableArray<ClassInfo> classes)
    {
        if (interfaces.IsDefaultOrEmpty | classes.IsDefaultOrEmpty) return; // nothing to do

        // each interface is declared across several partial files, so we get one (identical) entry per
        // partial declaration; structural equality lets us collapse those down to one per interface.
        var iKeyed = new Dictionary<KnownInterfaces, InterfaceInfo>();
        foreach (var t in interfaces)
        {
            if (t.KnownType is KnownInterfaces.None) continue;
            if (!iKeyed.ContainsKey(t.KnownType)) iKeyed.Add(t.KnownType, t);
        }

        var sb = new StringBuilder();
        var writer = new CodeWriter(sb);
        writer.NewLine().Append("// <auto-generated/>");
        writer.NewLine().Append("// AutoDatabaseGenerator - explicit interface implementations that funnel every");
        writer.NewLine().Append("// call through Execute(state, projection), capturing the arguments in a generated");
        writer.NewLine().Append("// state struct (one per unique parameter-type signature) to avoid per-call closures.");
        writer.NewLine().Append("#nullable enable"); // this needs to be explicit for code-gen
        writer.NewLine();
        foreach (var cls in classes.Distinct())
        {
            if (!string.IsNullOrWhiteSpace(cls.Namespace))
            {
                writer.NewLine().Append("namespace ").Append(cls.Namespace).NewLine().Append("{").Indent();
            }

            // unique parameter-type signatures encountered while emitting this class's methods;
            // keyed on the '|'-joined parameter types so distinct methods with the same shape share
            // one state struct. tupleDefs[i] holds a representative parameter list for _tuple{i}.
            var tupleIndex = new Dictionary<string, (int Index, bool NeedsMap)>();
            var tupleDefs = new List<BasicArray<ParamInfo>>();

            // every unique key/channel-bearing result type across all shapes; a single shared
            // singleton (emitted after the tuples) implements IRedisArgsResult<T> for each, so
            // tuples expose unmap dispatch via UnMapper without ever being cast to an interface
            var allReturns = new HashSet<string>();

            bool isFirst = true;
            writer.NewLine().Append("partial class ").Append(cls.Name);
            AppendInterfaceDeclaration(KnownInterfaces.IDatabase);
            AppendInterfaceDeclaration(KnownInterfaces.IDatabaseAsync);
            AppendInterfaceDeclaration(KnownInterfaces.IRedis);
            AppendInterfaceDeclaration(KnownInterfaces.IRedisAsync);
            writer.NewLine().Append("{").Indent();
            AppendInterfaceMethods(KnownInterfaces.IDatabase);
            AppendInterfaceMethods(KnownInterfaces.IDatabaseAsync);
            AppendInterfaceMethods(KnownInterfaces.IRedis);
            AppendInterfaceMethods(KnownInterfaces.IRedisAsync);
            AppendTupleTypes();

            writer.Outdent().NewLine().Append("}");

            if (!string.IsNullOrWhiteSpace(cls.Namespace))
            {
                writer.Outdent().NewLine().Append("}");
            }
            writer.NewLine();


            void AppendInterfaceDeclaration(KnownInterfaces knownType)
            {
                if ((cls.Interfaces & knownType) is 0 | !iKeyed.TryGetValue(knownType, out var iType)) return;
                writer.Append(isFirst ? " : " : ", ").Append("global::")
                    .Append(iType.Namespace).Append('.') .Append(iType.Name);
                isFirst = false;
            }

            void AppendInterfaceMethods(KnownInterfaces knownType)
            {
                if ((cls.Interfaces & knownType) is 0 | !iKeyed.TryGetValue(knownType, out var iType)) return;
                foreach (var method in iType.Methods)
                {
                    if (SkipMethod(method)) continue; // wonky by nature - left for the caller to implement manually

                    writer.NewLine().Append(method.ReturnType).Append(" global::")
                        .Append(iType.Namespace).Append('.').Append(iType.Name).Append('.').Append(method.Name).Append("(");
                    bool firstParam = true;
                    foreach (var p in method.Parameters.Span)
                    {
                        if (firstParam) firstParam = false;
                        else writer.Append(", ");
                        writer.Append(p.Type).Append(" ").Append(p.Name);
                    }
                    writer.Append(')').Indent().NewLine();

                    // async methods route to ExecuteAsync (its own retry policy); everything else uses
                    // Execute. The state struct is shared regardless of return type - keyed on parameter
                    // types only - so e.g. ArraySet and ArraySetAsync land on the same _tupleN. The return
                    // type is Task-stripped so unmapping is keyed on the inner result the retry machinery
                    // actually sees, not the Task<T> wrapper.
                    bool isAsync = IsAsync(method.ReturnType);
                    var simpleResult = StripTask(method.ReturnType);
                    bool needsMap = NeedsMap(simpleResult);
                    if (needsMap) allReturns.Add(simpleResult);
                    int tuple = GetTupleIndex(method.Parameters, needsMap);
                    writer.Append(isAsync ? "=> ExecuteAsync(new _tuple" : "=> Execute(new _tuple").Append(tuple).Append("(");
                    firstParam = true;
                    foreach (var p in method.Parameters.Span)
                    {
                        if (firstParam) firstParam = false;
                        else writer.Append(", ");
                        writer.Append(p.Name);
                    }
                    // the funnels take the state by readonly-ref (see AutoDatabase{Sync|Async}Operation) to avoid
                    // copying the larger state structs; a lambda only binds to an `in` parameter if it says so,
                    // hence the explicit modifier (C# 14 allows it without also naming the type)
                    writer.Append("), static (in state, inner) => inner.").Append(method.Name).Append("(");
                    for (int i = 0; i < method.Parameters.Length; i++)
                    {
                        if (i != 0) writer.Append(", ");
                        writer.Append("state.Arg").Append(i);
                    }
                    writer.Append("));").Outdent().NewLine();
                }
            }

            static string GetTupleKey(BasicArray<ParamInfo> parameters)
            {
                var keyBuilder = new StringBuilder();
                foreach (var p in parameters.Span)
                {
                    keyBuilder.Append(p.Type).Append('|'); // types-only key; no ref/out on this surface, so type is sufficient
                }

                return keyBuilder.ToString();
            }

            bool TupleNeedsMap(BasicArray<ParamInfo> parameters) =>
                tupleIndex.TryGetValue(GetTupleKey(parameters), out var found) && found.NeedsMap;

            int GetTupleIndex(BasicArray<ParamInfo> parameters, bool needsMap)
            {
                var key = GetTupleKey(parameters);
                int index;
                if (tupleIndex.TryGetValue(key, out var found))
                {
                    index = found.Index;
                    if (needsMap && !found.NeedsMap)
                    {
                        found.NeedsMap = true;
                        tupleIndex[key] = found;
                    }
                }
                else
                {
                    index = tupleDefs.Count;
                    tupleIndex.Add(key, (index, needsMap));
                    tupleDefs.Add(parameters);
                }

                return index;
            }

            const string CommandFlagsType = "StackExchange.Redis.CommandFlags";
            const string RedisKeyType = "StackExchange.Redis.RedisKey";
            const string RedisChannelType = "StackExchange.Redis.RedisChannel";
            // types that carry key(s) internally without "RedisKey" in their name, so the
            // substring check below won't catch them; they rely on a Map extension method
            const string StreamPositionType = "StackExchange.Redis.StreamPosition";
            // the Execute/ExecuteAsync escape hatch boxes keys/channels inside a loosely-typed
            // arg list; these route through a Map extension that unboxes and rewrites matches
            const string ScriptArgArrayType = "object[]";
            const string ScriptArgCollectionType = "System.Collections.Generic.ICollection<object>";

            static bool NeedsMap(string name) =>
                name.IndexOf(RedisKeyType, StringComparison.Ordinal) >= 0
                || name.IndexOf(RedisChannelType, StringComparison.Ordinal) >= 0
                || name.IndexOf(StreamPositionType, StringComparison.Ordinal) >= 0
                || name == ScriptArgArrayType
                || name == ScriptArgCollectionType
                || name.IndexOf("ListPopResult", StringComparison.Ordinal) >= 0
                || name.IndexOf("SortedSetPopResult", StringComparison.Ordinal) >= 0
                || name == ScriptArgCollectionType + "?";

            void AppendTupleTypes()
            {
                for (int i = 0; i < tupleDefs.Count; i++)
                {
                    var raw = tupleDefs[i];
                    bool needsMap = TupleNeedsMap(raw);
                    var parameters = raw.Span;

                    // key/channel-bearing fields are mutable only when something can rewrite them (i.e. the
                    // owning database is an IRedisArgsMutator); otherwise every field is readonly
                    // a non-mutator's captured args never change after capture, so the struct itself can be
                    // readonly - which also guarantees no defensive copies when read through an `in` ref
                    writer.NewLine().NewLine().Append(cls.IsMutator ? "private struct _tuple" : "private readonly struct _tuple").Append(i).Append("(");
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (p != 0) writer.Append(", ");
                        writer.Append(parameters[p].Type).Append(" arg").Append(p);
                    }

                    // captured args are a plain struct unless the owning database rewrites keys, in which
                    // case the mapping members - and the interface that carries them - are emitted below.
                    // A captured CommandFlags is surfaced through IFlaggedRedisArgs regardless, so that a
                    // funnel holding an opaque TState can still see fire-and-forget; the two compose.
                    writer.Append(")");
                    int flagsArg = -1;
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        // suffix rather than substring: this must not match a nullable, an array, or a
                        // container *of* flags, none of which the funnel could read as a plain value
                        if (parameters[p].Type.EndsWith(CommandFlagsType, StringComparison.Ordinal))
                        {
                            flagsArg = p;
                            break;
                        }
                    }

                    bool firstBase = true;
                    if (cls.IsMutator)
                    {
                        writer.Append(" : global::StackExchange.Redis.IMappableRedisArgs");
                        firstBase = false;
                    }

                    if (flagsArg >= 0)
                    {
                        writer.Append(firstBase ? " : " : ", ").Append("global::StackExchange.Redis.IFlaggedRedisArgs");
                    }
                    writer.NewLine().Append("{").Indent();
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        writer.NewLine().Append("public ").Append(cls.IsMutator && NeedsMap(parameters[p].Type) ? "" : "readonly ").Append(parameters[p].Type)
                            .Append(" Arg").Append(p).Append(" = arg").Append(p).Append(";");
                    }

                    if (flagsArg >= 0)
                    {
                        // explicit implementation, so it cannot collide with a captured argument's own name;
                        // readonly only on a mutator's struct, since a readonly struct's members already are
                        writer.NewLine().Append(cls.IsMutator ? "readonly " : "")
                            .Append("global::StackExchange.Redis.CommandFlags global::StackExchange.Redis.IFlaggedRedisArgs.Flags => Arg")
                            .Append(flagsArg).Append(";");
                    }

                    if (!cls.IsMutator)
                    {
                        // nothing rewrites keys here, so there is no Map/UnMapper to emit at all
                        writer.Outdent().NewLine().Append("}");
                        continue;
                    }

                    // Map rewrites each scalar key/channel field directly, and defers container or
                    // loosely-typed fields to a matching Map extension method
                    writer.NewLine().Append("public void Map(global::StackExchange.Redis.IRedisArgsMutator mutator)")
                        .NewLine().Append("{").Indent();
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (NeedsMap(parameters[p].Type))
                        {
                            // key/channel-bearing container (or the loosely-typed script arg list); it
                            // is the library's job to ensure a suitable Map extension method exists
                            writer.NewLine().Append("Arg").Append(p).Append(" = mutator.Map(Arg").Append(p).Append(");");
                        }
                    }
                    writer.Outdent().NewLine().Append("}");

                    // tuples with key/channel-bearing results point UnMapper at the shared singleton
                    // (which knows how to unmap every such result type); the rest return null so the
                    // Execute helper skips unmapping entirely - and neither path boxes the struct
                    writer.NewLine().Append("public readonly object? UnMapper => ")
                        .Append(needsMap ? "_UnMapper.Instance;" : "null;");

                    writer.Outdent().NewLine().Append("}");
                }

                if (cls.IsMutator && allReturns.Count is not 0)
                {
                    // sorted for deterministic (cache-stable) output
                    var ordered = new List<string>(allReturns);
                    ordered.Sort(StringComparer.Ordinal);

                    writer.NewLine().NewLine().Append("private sealed class _UnMapper").Indent();
                    bool firstIface = true;
                    foreach (var retType in ordered)
                    {
                        writer.NewLine().Append(firstIface ? ": " : ", ")
                            .Append("global::StackExchange.Redis.IRedisArgsResult<").Append(retType).Append(">");
                        firstIface = false;
                    }
                    writer.Outdent().NewLine().Append("{").Indent();
                    writer.NewLine().Append("public static readonly _UnMapper Instance = new();");
                    foreach (var retType in ordered)
                    {
                        writer.NewLine().NewLine().Append(retType).Append(' ')
                            .Append("global::StackExchange.Redis.IRedisArgsResult<")
                            .Append(retType)
                            .Append(">.UnMap(global::StackExchange.Redis.IRedisArgsMutator mutator, ")
                            .Append(retType).Append(" value)")
                            .Indent().NewLine().Append("=> mutator.UnMap(value);").Outdent();
                    }
                    writer.Outdent().NewLine().Append("}");
                }
            }
        }
        writer.NewLine();

        ctx.AddSource("AutoDatabase.generated.cs", sb.ToString());
    }
}
