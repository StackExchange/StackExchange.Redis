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
                        Name: "AutoDatabaseAttribute"
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
        writer.NewLine().Append("// AutoDatabaseGenerator diagnostic dump - what ExtractInterfaceMethods found.");
        writer.NewLine().Append("// This file is informational only (everything is in comments); no real code is emitted yet.");
        writer.NewLine().Append("#nullable enable"); // this needs to be explicit for code-gen
        writer.NewLine();
        foreach (var cls in classes.Distinct())
        {
            if (!string.IsNullOrWhiteSpace(cls.Namespace))
            {
                writer.NewLine().Append("namespace ").Append(cls.Namespace).NewLine().Append("{").Indent();
            }

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
                    writer.NewLine().Append(method.ReturnType).Append(" global::")
                        .Append(iType.Namespace).Append('.').Append(iType.Name).Append('.').Append(method.Name);
                    if (!method.TypeArgs.IsEmpty)
                    {
                        bool firstT = true;
                        writer.Append("<");
                        foreach (var t in method.TypeArgs)
                        {
                            if (firstT) firstT = false;
                            else writer.Append(", ");
                            writer.Append(t);
                        }
                        writer.Append(">");
                    }
                    writer.Append("(");
                    bool firstParam = true;
                    foreach (var p in method.Parameters.Span)
                    {
                        if (firstParam) firstParam = false;
                        else writer.Append(", ");
                        writer.Append(p.Type).Append(" ").Append(p.Name);
                    }
                    writer.Append(')').Indent().NewLine().Append("=> throw new global::System.NotImplementedException();")
                        .Outdent().NewLine();
                }
            }
        }
        writer.NewLine();

        ctx.AddSource("AutoDatabase.generated.cs", sb.ToString());
    }
}
