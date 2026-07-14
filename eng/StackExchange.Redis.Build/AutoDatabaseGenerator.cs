using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// readable names for the info we gather; kept as tuples (+ BasicArray) so they have
// value equality and play nicely with incremental generator caching.
using ParamInfo = (string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default);
using MethodInfo = (string Name, string ReturnType, StackExchange.Redis.Build.BasicArray<(string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default)> Parameters, StackExchange.Redis.Build.BasicArray<string> TypeArgs);
using InterfaceInfo = (string Name, string Namespace, StackExchange.Redis.Build.AutoDatabaseGenerator.KnownInterfaces KnownType, StackExchange.Redis.Build.BasicArray<(string Name, string ReturnType, StackExchange.Redis.Build.BasicArray<(string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default)> Parameters, StackExchange.Redis.Build.BasicArray<string> TypeArgs)> Methods);
using ClassInfo = (string Name, string Namespace, StackExchange.Redis.Build.AutoDatabaseGenerator.KnownInterfaces Interfaces);

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

        var ns = cls.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return (cls.Name, ns, known);
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
    //  - streaming returns (IEnumerable<T> / IAsyncEnumerable<T>) whose execution is deferred
    private static bool SkipMethod(MethodInfo method)
        => !method.TypeArgs.IsEmpty
        || method.Name.Contains("Wait")
        || method.ReturnType.StartsWith("System.Collections.Generic.IEnumerable<", StringComparison.Ordinal)
        || method.ReturnType.StartsWith("System.Collections.Generic.IAsyncEnumerable<", StringComparison.Ordinal);

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
            var tupleIndex = new Dictionary<string, int>();
            var tupleDefs = new List<BasicArray<ParamInfo>>();

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

                    // Task / Task<T> methods route to ExecuteAsync (its own retry policy); everything
                    // else uses Execute. The state struct is shared regardless of return type - keyed on
                    // parameter types only - so e.g. ArraySet and ArraySetAsync land on the same _tupleN.
                    bool isAsync = method.ReturnType.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal);
                    int tuple = GetTupleIndex(method.Parameters);
                    writer.Append(isAsync ? "=> ExecuteAsync(new _tuple" : "=> Execute(new _tuple").Append(tuple).Append("(");
                    firstParam = true;
                    foreach (var p in method.Parameters.Span)
                    {
                        if (firstParam) firstParam = false;
                        else writer.Append(", ");
                        writer.Append(p.Name);
                    }
                    writer.Append("), static (state, inner) => inner.").Append(method.Name).Append("(");
                    for (int i = 0; i < method.Parameters.Length; i++)
                    {
                        if (i != 0) writer.Append(", ");
                        writer.Append("state.Arg").Append(i);
                    }
                    writer.Append("));").Outdent().NewLine();
                }
            }

            int GetTupleIndex(BasicArray<ParamInfo> parameters)
            {
                var keyBuilder = new StringBuilder();
                foreach (var p in parameters.Span)
                {
                    keyBuilder.Append(p.Type).Append('|'); // types-only key; no ref/out on this surface, so type is sufficient
                }

                var key = keyBuilder.ToString();
                if (!tupleIndex.TryGetValue(key, out var index))
                {
                    index = tupleDefs.Count;
                    tupleIndex.Add(key, index);
                    tupleDefs.Add(parameters);
                }

                return index;
            }

            void AppendTupleTypes()
            {
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
                for (int i = 0; i < tupleDefs.Count; i++)
                {
                    var parameters = tupleDefs[i].Span;

                    // locate the (single) CommandFlags argument, if any, to back IRedisArgs.Flags
                    int flagsArg = -1;
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (parameters[p].Type == CommandFlagsType)
                        {
                            flagsArg = p;
                            break;
                        }
                    }

                    // fields are mutable: Map rewrites key/channel fields and Flags has a setter
                    writer.NewLine().NewLine().Append("private struct _tuple").Append(i).Append("(");
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (p != 0) writer.Append(", ");
                        writer.Append(parameters[p].Type).Append(" arg").Append(p);
                    }
                    writer.Append(") : global::StackExchange.Redis.IRedisArgs").NewLine().Append("{").Indent();
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        writer.NewLine().Append("public ").Append(parameters[p].Type)
                            .Append(" Arg").Append(p).Append(" = arg").Append(p).Append(";");
                    }

                    // Flags maps onto the CommandFlags field when present, else a synthesized default
                    writer.NewLine().Append("public global::StackExchange.Redis.CommandFlags Flags");
                    if (flagsArg >= 0)
                    {
                        writer.Append(" { readonly get => Arg").Append(flagsArg).Append("; set => Arg").Append(flagsArg).Append(" = value; }");
                    }
                    else
                    {
                        writer.Append(" { get; set; }");
                    }

                    // Map rewrites each scalar key/channel field directly, and defers container or
                    // loosely-typed fields to a matching Map extension method
                    writer.NewLine().Append("public void Map(global::StackExchange.Redis.IRedisArgsMutator mutator)")
                        .NewLine().Append("{").Indent();
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (parameters[p].Type == RedisKeyType)
                        {
                            writer.NewLine().Append("Arg").Append(p).Append(" = mutator.MapKey(Arg").Append(p).Append(");");
                        }
                        else if (parameters[p].Type == RedisChannelType)
                        {
                            writer.NewLine().Append("Arg").Append(p).Append(" = mutator.MapChannel(Arg").Append(p).Append(");");
                        }
                        else if (parameters[p].Type.IndexOf(RedisKeyType, StringComparison.Ordinal) >= 0
                            || parameters[p].Type.IndexOf(StreamPositionType, StringComparison.Ordinal) >= 0
                            || parameters[p].Type == ScriptArgArrayType
                            || parameters[p].Type == ScriptArgCollectionType
                            || parameters[p].Type == ScriptArgCollectionType + "?")
                        {
                            // key/channel-bearing container (or the loosely-typed script arg list); it
                            // is the library's job to ensure a suitable Map extension method exists
                            writer.NewLine().Append("Arg").Append(p).Append(" = mutator.Map(Arg").Append(p).Append(");");
                        }
                    }
                    writer.Outdent().NewLine().Append("}");

                    writer.Outdent().NewLine().Append("}");
                }
            }
        }
        writer.NewLine();

        ctx.AddSource("AutoDatabase.generated.cs", sb.ToString());
    }
}
