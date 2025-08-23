using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StackExchange.Redis.Build;

[Generator(LanguageNames.CSharp)]
public class RespCommandGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var literals = context.SyntaxProvider
            .CreateSyntaxProvider(Predicate, Transform)
            .Where(pair => pair.MethodName is { Length: > 0 })
            .Collect();

        context.RegisterSourceOutput(literals, Generate);
    }

    private bool Predicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        // looking for [FastHash] partial static class Foo { }
        if (node is MethodDeclarationSyntax decl
            && decl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            foreach (var attribList in decl.AttributeLists)
            {
                foreach (var attrib in attribList.Attributes)
                {
                    if (attrib.Name.ToString() is "RespCommandAttribute" or "RespCommand") return true;
                }
            }
        }

        return false;
    }

    private static string GetFullName(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string GetName(INamedTypeSymbol type)
    {
        if (type.ContainingType is null) return type.Name;
        var stack = new Stack<string>();
        while (true)
        {
            stack.Push(type.Name);
            if (type.ContainingType is null) break;
            type = type.ContainingType;
        }

        var sb = new StringBuilder(stack.Pop());
        while (stack.Count != 0)
        {
            sb.Append('.').Append(stack.Pop());
        }

        return sb.ToString();
    }

    private (string Namespace, string TypeName, string ReturnType, string MethodName, string Command, int KeyIndex,
        ImmutableArray<(string Type, string Name, string Modifiers)> Parameters, string TypeModifiers, string
        MethodModifiers, string Context) Transform(
            GeneratorSyntaxContext ctx,
            CancellationToken cancellationToken)
    {
        // extract the name and value (defaults to name, but can be overridden via attribute) and the location
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not IMethodSymbol method) return default;
        if (!(method.IsPartialDefinition && method.PartialImplementationPart is null)) return default;

        string returnType;
        if (method.ReturnsVoid)
        {
            returnType = "";
        }
        else if (method.ReturnType is INamedTypeSymbol named)
        {
            returnType = GetFullName(named);
        }
        else
        {
            return default;
        }

        string ns = "", parentType = "";
        if (method.ContainingType is { } containingType)
        {
            parentType = GetName(containingType);
            ns = containingType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }
        else if (method.ContainingNamespace is { } containingNamespace)
        {
            ns = containingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        string value = method.Name.ToLowerInvariant();
        foreach (var attrib in method.GetAttributes())
        {
            if (attrib.AttributeClass?.Name == "RespCommandAttribute")
            {
                if (attrib.ConstructorArguments.Length == 1)
                {
                    if (attrib.ConstructorArguments[0].Value?.ToString() is { Length: > 0 } val)
                    {
                        value = val;
                        break;
                    }
                }
            }
        }

        int keyIndex = -1, fallbackKeyIndex = -1;
        var parameters =
            ImmutableArray.CreateBuilder<(string Type, string Name, string Modifiers)>(method.Parameters.Length);

        // get context from the available fields
        string? context = null;
        foreach (var member in method.ContainingType.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field && IsRespContext(field.Type))
            {
                context = field.Name;
                break;
            }
        }

        if (context is null)
        {
            // get context from primary constructor (actually, we look at all constructors,
            // and just hope that the one that matches: works!)
            foreach (var ctor in method.ContainingType.Constructors)
            {
                if (ctor.IsStatic) continue;
                foreach (var param in ctor.Parameters)
                {
                    if (IsRespContext(param.Type))
                    {
                        context = param.Name;
                        break;
                    }
                }

                if (context is not null) break;
            }
        }

        if (context is null)
        {
            // last ditch, get context from properties
            foreach (var member in method.ContainingType.GetMembers())
            {
                if (member is IPropertySymbol { IsStatic: false } prop
                    && IsRespContext(prop.Type) && prop.GetMethod is not null)
                {
                    context = prop.Name;
                    break;
                }
            }
        }

        foreach (var param in method.Parameters)
        {
            if (param.Type is not INamedTypeSymbol named) return default; // I can't work with that
            if (keyIndex == -1 && IsKey(param))
            {
                keyIndex = parameters.Count;
            }

            if (fallbackKeyIndex == -1 && param.Name == "key")
            {
                fallbackKeyIndex = parameters.Count;
            }

            string modifiers = param.RefKind switch
            {
                RefKind.None => "",
                RefKind.In => "in ",
                RefKind.Out => "out ",
                RefKind.Ref => "ref ",
                _ => "",
            };

            if (param.Ordinal == 0 && method.IsExtensionMethod)
            {
                modifiers = "this " + modifiers;
                if (IsRespContext(param.Type))
                {
                    context = param.Name;
                }
            }

            parameters.Add((GetFullName(named), param.Name, modifiers));
        }

        static bool IsRespContext(ITypeSymbol type)
            => type is INamedTypeSymbol
            {
                Name: "RespContext",
                ContainingNamespace: { Name: "Resp", ContainingNamespace.IsGlobalNamespace: true }
            };

        if (keyIndex == -1) keyIndex = fallbackKeyIndex;
        var syntax = (MethodDeclarationSyntax)ctx.Node;
        return (ns, parentType, returnType, method.Name, value, keyIndex, parameters.ToImmutable(),
            TypeModifiers(method.ContainingType), syntax.Modifiers.ToString(), context ?? "");

        static string TypeModifiers(INamedTypeSymbol type)
        {
            foreach (var symbol in type.DeclaringSyntaxReferences)
            {
                var syntax = symbol.GetSyntax();
                if (syntax is TypeDeclarationSyntax typeDeclaration)
                {
                    var mods = typeDeclaration.Modifiers.ToString();
                    return syntax switch
                    {
                        InterfaceDeclarationSyntax => $"{mods} interface",
                        StructDeclarationSyntax => $"{mods} struct",
                        _ => $"{mods} class",
                    };
                }
            }

            return "class"; // wut?
        }
    }

    private bool IsKey(IParameterSymbol param)
    {
        foreach (var attrib in param.GetAttributes())
        {
            if (attrib.AttributeClass?.Name == "KeyAttribute") return true;
        }

        return false;
    }

    private string GetVersion()
    {
        var asm = GetType().Assembly;
        if (asm.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false).FirstOrDefault() is
            AssemblyFileVersionAttribute { Version: { Length: > 0 } } version)
        {
            return version.Version;
        }

        return asm.GetName().Version?.ToString() ?? "??";
    }

    private void Generate(
        SourceProductionContext ctx,
        ImmutableArray<(string Namespace, string TypeName, string ReturnType, string MethodName, string Command, int
            KeyIndex, ImmutableArray<(string Type, string Name, string Modifiers)> Parameters, string TypeModifiers,
            string
            MethodModifiers, string Context)> methods)
    {
        if (methods.IsDefaultOrEmpty) return;

        var sb = new StringBuilder("// <auto-generated />")
            .AppendLine().Append("// ").Append(GetType().Name).Append(" v").Append(GetVersion()).AppendLine();

        bool first;
        int indent = 0;

        // find the unique param types, so we can build helpers
        Dictionary<(ImmutableArray<(string Type, string Name, string Modifiers)> Parameters, int KeyIndex), string>
            formatters =
                new(FormatterComparer.Default);
        static bool IsThis(string modifier) => modifier.StartsWith("this ");

        static bool UseTuple(ImmutableArray<(string Type, string Name, string Modifiers)> parameters)
        {
            if (parameters.IsDefaultOrEmpty) return false;
            if (parameters.Length < 2) return false;
            if (parameters.Length == 2 && IsThis(parameters[0].Modifiers)) return false;
            return true;
        }

        foreach (var method in methods)
        {
            if (!UseTuple(method.Parameters))
            {
                continue; // consumer should add their own extension method for the target type
            }

            var key = (method.Parameters, method.KeyIndex);
            if (!formatters.ContainsKey(key))
            {
                formatters.Add(key, $"__RespFormatter_{formatters.Count}");
            }
        }

        StringBuilder NewLine() => sb.AppendLine().Append(' ', indent * 4);
        NewLine().Append("using System;");
        NewLine().Append("using System.Threading.Tasks;");
        foreach (var grp in methods.GroupBy(l => (l.Namespace, l.TypeName, l.TypeModifiers)))
        {
            NewLine();
            int braces = 0;
            if (!string.IsNullOrWhiteSpace(grp.Key.Namespace))
            {
                NewLine().Append("namespace ").Append(grp.Key.Namespace);
                NewLine().Append("{");
                indent++;
                braces++;
            }

            if (!string.IsNullOrWhiteSpace(grp.Key.TypeName))
            {
                if (grp.Key.TypeName.Contains('.')) // nested types
                {
                    var toks = grp.Key.TypeName.Split('.');
                    for (var i = 0; i < toks.Length; i++)
                    {
                        var part = toks[i];
                        if (i == toks.Length - 1)
                        {
                            NewLine().Append(grp.Key.TypeModifiers).Append(' ').Append(part);
                        }
                        else
                        {
                            NewLine().Append("partial class ").Append(part);
                        }

                        NewLine().Append("{");
                        indent++;
                        braces++;
                    }
                }
                else
                {
                    NewLine().Append(grp.Key.TypeModifiers).Append(' ').Append(grp.Key.TypeName);
                    NewLine().Append("{");
                    indent++;
                    braces++;
                }
            }

            foreach (var method in grp)
            {
                string? formatter = InbuiltFormatter(method.Parameters)
                                    ?? (formatters.TryGetValue((method.Parameters, method.KeyIndex), out var tmp)
                                        ? $"{tmp}.Default"
                                        : null);

                // perform string escaping on the generated value (this includes the quotes, note)
                var csValue = SyntaxFactory
                    .LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(method.Command))
                    .ToFullString();

                sb = NewLine().Append(method.MethodModifiers).Append(' ')
                    .Append(string.IsNullOrEmpty(method.ReturnType) ? "void" : method.ReturnType)
                    .Append(' ').Append(method.MethodName).Append("(");
                first = true;
                foreach (var param in method.Parameters)
                {
                    if (!first) sb.Append(", ");
                    first = false;

                    sb.Append(param.Modifiers).Append(param.Type).Append(' ').Append(param.Name);
                }

                sb.Append(")");
                indent++;
                sb = NewLine().Append("=> ");
                if (string.IsNullOrWhiteSpace(method.Context))
                {
                    sb.Append("throw new NotSupportedException(\"No RespContext available\");");
                }
                else
                {
                    sb.Append(method.Context).Append(".Command(").Append(csValue).Append("u8");
                    if (!method.Parameters.IsDefaultOrEmpty)
                    {
                        sb.Append(", ");
                        WriteTuple(method.Parameters, sb, TupleMode.Values);

                        if (formatter is not null)
                        {
                            sb.Append(", ").Append(formatter);
                        }
                    }

                    sb.Append(").Wait");
                    if (!string.IsNullOrWhiteSpace(method.ReturnType))
                    {
                        sb.Append('<').Append(method.ReturnType).Append('>');
                    }

                    sb.Append("(").Append(InbuiltParser(method.ReturnType)).Append(");");
                }

                indent--;
                NewLine();

                sb = NewLine().Append(RemovePartial(method.MethodModifiers)).Append(" ValueTask");
                if (!string.IsNullOrWhiteSpace(method.ReturnType))
                {
                    sb.Append('<').Append(method.ReturnType).Append('>');
                }

                sb.Append(' ').Append(method.MethodName).Append("Async(");
                first = true;
                foreach (var param in method.Parameters)
                {
                    if (!first) sb.Append(", ");
                    first = false;

                    sb.Append(param.Modifiers).Append(param.Type).Append(' ').Append(param.Name);
                }

                sb.Append(")");
                indent++;
                sb = NewLine().Append("=> ");
                if (string.IsNullOrWhiteSpace(method.Context))
                {
                    sb.Append("throw new NotSupportedException(\"No RespContext available\");");
                }
                else
                {
                    sb.Append(method.Context).Append(".Command(").Append(csValue).Append("u8");
                    if (!method.Parameters.IsDefaultOrEmpty)
                    {
                        sb.Append(", ");
                        WriteTuple(method.Parameters, sb, TupleMode.Values);

                        if (formatter is not null)
                        {
                            sb.Append(", ").Append(formatter);
                        }
                    }

                    sb.Append(").AsValueTask");
                    if (!string.IsNullOrWhiteSpace(method.ReturnType))
                    {
                        sb.Append('<').Append(method.ReturnType).Append('>');
                    }

                    sb.Append("(").Append(InbuiltParser(method.ReturnType)).Append(");");
                }

                indent--;
                NewLine();
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                indent--;
                NewLine().Append("}");
            }

            NewLine();
        }

        foreach (var tuple in formatters)
        {
            var keyIndex = tuple.Key.KeyIndex;
            var parameters = tuple.Key.Parameters;
            var name = tuple.Value;
            NewLine();
            sb = NewLine().Append("sealed file class ").Append(name).Append(" : Resp.IRespFormatter<");
            WriteTuple(parameters, sb, TupleMode.SyntheticNames);
            sb.Append('>');
            NewLine().Append("{");
            indent++;
            NewLine().Append("public static readonly ").Append(name).Append(" Default = new();");
            NewLine();

            sb = NewLine()
                .Append("public void Format(scoped ReadOnlySpan<byte> command, ref Resp.RespWriter writer, in ");
            WriteTuple(parameters, sb, TupleMode.SyntheticNames);
            sb.Append(" request)");
            NewLine().Append("{");
            indent++;
            sb = NewLine().Append("writer.WriteCommand(command, ").Append(parameters.Length);
            if (keyIndex >= 0) sb.Append(", keyIndex: ").Append(keyIndex);
            sb.Append(");");
            if (parameters.Length == 1)
            {
                NewLine().Append("writer.WriteBulkString(request);");
            }
            else
            {
                int index = 0;
                foreach (var parameter in parameters)
                {
                    NewLine().Append("writer.WriteBulkString(request.Arg").Append(index++).Append(");");
                }
            }

            indent--;
            NewLine().Append("}");
            indent--;
            NewLine().Append("}");
        }

        NewLine();
        ctx.AddSource(GetType().Name + ".generated.cs", sb.ToString());

        static void WriteTuple(
            ImmutableArray<(string Type, string Name, string Modifiers)> parameters,
            StringBuilder sb,
            TupleMode mode)
        {
            if (parameters.IsDefaultOrEmpty) return;
            if (!UseTuple(parameters))
            {
                var p = parameters[0];
                if (IsThis(p.Modifiers))
                {
                    p = parameters[1];
                }

                sb.Append(mode == TupleMode.Values ? p.Name : p.Type);
                return;
            }

            sb.Append('(');
            int index = 0;
            foreach (var param in parameters)
            {
                if (IsThis(param.Modifiers)) continue; // note don't increase index
                if (index != 0) sb.Append(", ");

                switch (mode)
                {
                    case TupleMode.Values:
                        sb.Append(param.Name);
                        break;
                    case TupleMode.AnonTuple:
                        sb.Append(param.Type);
                        break;
                    case TupleMode.NamedTuple:
                        sb.Append(param.Type).Append(' ').Append(param.Name);
                        break;
                    case TupleMode.SyntheticNames:
                        sb.Append(param.Type).Append(" Arg").Append(index);
                        break;
                }

                index++;
            }

            sb.Append(')');
        }
    }

    private static string? InbuiltFormatter(ImmutableArray<(string Type, string Name, string Modifiers)> parameters)
    {
        if (!parameters.IsDefaultOrEmpty && parameters.Length == 1)
        {
            return InbuiltFormatter(parameters[0].Type);
        }

        return null;
    }

    private static string? InbuiltFormatter(string type) => type switch
    {
        "string" => "Resp.RespFormatters.String",
        "int" => "Resp.RespFormatters.Int32",
        "long" => "Resp.RespFormatters.Int64",
        "float" => "Resp.RespFormatters.Single",
        "double" => "Resp.RespFormatters.Double",
        _ => null,
    };

    private static string? InbuiltParser(string type, bool explicitSuccess = false) => type switch
    {
        "" when explicitSuccess => "Resp.RespParsers.Success",
        "string" => "Resp.RespParsers.String",
        "int" => "Resp.RespParsers.Int32",
        "long" => "Resp.RespParsers.Int64",
        "float" => "Resp.RespParsers.Single",
        "double" => "Resp.RespParsers.Double",
        "int?" => "Resp.RespParsers.NullableInt32",
        "long?" => "Resp.RespParsers.NullableInt64",
        "float?" => "Resp.RespParsers.NullableSingle",
        "double?" => "Resp.RespParsers.NullableDouble",
        _ => null,
    };

    private enum TupleMode
    {
        AnonTuple,
        NamedTuple,
        Values,
        SyntheticNames,
    }

    private static string RemovePartial(string modifiers)
    {
        if (string.IsNullOrWhiteSpace(modifiers) || !modifiers.Contains("partial")) return modifiers;
        if (modifiers == "partial") return "";
        if (modifiers.StartsWith("partial ")) return modifiers.Substring(8);
        if (modifiers.EndsWith(" partial")) return modifiers.Substring(0, modifiers.Length - 8);
        return modifiers.Replace(" partial ", " ");
    }
}

// compares whether a formatter can be shared, which depends on the key index and types (not names)
internal sealed class
    FormatterComparer : IEqualityComparer<(ImmutableArray<(string Type, string Name, string Modifiers)> Parameters, int
    KeyIndex)>
{
    private FormatterComparer() { }
    public static readonly FormatterComparer Default = new();

    public bool Equals(
        (ImmutableArray<(string Type, string Name, string Modifiers)> Parameters, int KeyIndex) x,
        (ImmutableArray<(string Type, string Name, string Modifiers)> Parameters, int KeyIndex) y)
    {
        if (x.KeyIndex != y.KeyIndex) return false;
        if (x.Parameters.Length != y.Parameters.Length) return false;
        for (int i = 0; i < x.Parameters.Length; i++)
        {
            if (x.Parameters[i].Type != y.Parameters[i].Type) return false;
        }

        return true;
    }

    public int GetHashCode((ImmutableArray<(string Type, string Name, string Modifiers)> Parameters, int KeyIndex) obj)
    {
        var hash = obj.KeyIndex & obj.Parameters.Length;
        foreach (var p in obj.Parameters)
        {
            hash ^= p.Type.GetHashCode();
        }

        return hash;
    }
}
