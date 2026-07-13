using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// readable names for the info we gather; kept as tuples (+ BasicArray) so they have
// value equality and play nicely with incremental generator caching.
using ParamInfo = (string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default);
using MethodInfo = (string Name, string ReturnType, StackExchange.Redis.Build.BasicArray<(string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default)> Parameters);
using InterfaceInfo = (string Name, string Namespace, StackExchange.Redis.Build.BasicArray<(string Name, string ReturnType, StackExchange.Redis.Build.BasicArray<(string Name, string Type, Microsoft.CodeAnalysis.RefKind RefKind, bool IsParams, bool IsOptional, bool HasDefault, string? Default)> Parameters)> Methods);
using ClassInfo = (string Name, string Namespace, bool IDatabase, bool IDatabaseAsync);

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

    private static bool FastIndexFilter(InterfaceDeclarationSyntax decl) // limit to IDatabase, IDatabaseAsync
        => decl.Identifier.ValueText is "IDatabase" or "IDatabaseAsync";

    private static bool FastClassFilter(ClassDeclarationSyntax decl) // limit to IDatabase, IDatabaseAsync
        => decl.AttributeLists.Any(x => x.Attributes.Any(x => x.Name.ToString() is "AutoDatabase" or "AutoDatabaseAttribute"));

    private static bool IsOurInterface(INamedTypeSymbol symbol) =>
        symbol is
        {
            TypeKind: TypeKind.Interface,
            Name: "IDatabase" or "IDatabaseAsync",
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

        var methods = ImmutableArray.CreateBuilder<MethodInfo>();
        foreach (var member in iface.GetMembers())
        {
            cancel.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method) continue;

            var parameters = new BasicArray<ParamInfo>.Builder(method.Parameters.Length);
            foreach (var p in method.Parameters)
            {
                parameters.Add((
                    Name: p.Name,
                    Type: p.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    RefKind: p.RefKind,
                    IsParams: p.IsParams,
                    IsOptional: p.IsOptional,
                    HasDefault: p.HasExplicitDefaultValue,
                    Default: p.HasExplicitDefaultValue ? FormatDefault(p.ExplicitDefaultValue) : null));
            }

            methods.Add((
                Name: method.Name,
                ReturnType: method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                Parameters: parameters.Build()));
        }

        var ns = iface.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var methodArray = new BasicArray<MethodInfo>.Builder(methods.Count);
        foreach (var m in methods) methodArray.Add(m);
        return (iface.Name, ns, methodArray.Build());
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

        bool db = false, dba = false;
        foreach (var iFace in cls.Interfaces)
        {
            if (IsOurInterface(iFace))
            {
                switch (iFace.Name)
                {
                    case "IDatabase":
                        db = true;
                        break;
                    case "IDatabaseAsync":
                        dba = true;
                        break;
                }
            }
        }

        var ns = cls.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return (cls.Name, ns, db, dba);
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
        if (interfaces.IsDefaultOrEmpty) return; // nothing to do

        // each interface is declared across several partial files, so we get one (identical) entry per
        // partial declaration; structural equality lets us collapse those down to one per interface.
        var iKeyed = new Dictionary<string, InterfaceInfo>(StringComparer.Ordinal);
        foreach (var t in interfaces)
        {
            if (!iKeyed.ContainsKey(t.Name)) iKeyed.Add(t.Name, t);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// AutoDatabaseGenerator diagnostic dump - what ExtractInterfaceMethods found.");
        sb.AppendLine("// This file is informational only (everything is in comments); no real code is emitted yet.");
        sb.AppendLine();

        foreach (var cls in classes.Distinct())
        {
            sb.Append("// ==== ").Append(cls.Name).Append(" db: ").Append(cls.IDatabase).Append(" dba: ").Append(cls.IDatabaseAsync).AppendLine();
        }

        foreach (var iface in iKeyed.Values)
        {
            sb.Append("// ==== ").Append(iface.Namespace).Append('.').Append(iface.Name)
                .Append(" (").Append(iface.Methods.Length).AppendLine(" methods) ====");
            foreach (var method in iface.Methods.Span)
            {
                sb.Append("//   ").Append(method.ReturnType).Append(' ').Append(method.Name).AppendLine("(");
                for (int i = 0; i < method.Parameters.Length; i++)
                {
                    ref readonly var p = ref method.Parameters[i];
                    sb.Append("//       [").Append(i).Append("] ");
                    if (p.RefKind != RefKind.None) sb.Append(p.RefKind.ToString().ToLowerInvariant()).Append(' ');
                    if (p.IsParams) sb.Append("params ");
                    sb.Append(p.Type).Append(' ').Append(p.Name);
                    if (p.IsOptional) sb.Append(" [optional]");
                    if (p.HasDefault) sb.Append(" = ").Append(p.Default);
                    sb.AppendLine();
                }

                sb.AppendLine("//   )");
            }

            sb.AppendLine("//");
        }

        ctx.AddSource("AutoDatabase.Diagnostics.g.cs", sb.ToString());
    }
}
