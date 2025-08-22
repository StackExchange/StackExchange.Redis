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
        ImmutableArray<(string Type, string Name)> Parameters, string TypeModifiers, string MethodModifiers) Transform(
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
        var parameters = ImmutableArray.CreateBuilder<(string Type, string Name)>(method.Parameters.Length);
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

            parameters.Add((GetFullName(named), param.Name));
        }

        if (keyIndex == -1) keyIndex = fallbackKeyIndex;
        var syntax = (MethodDeclarationSyntax)ctx.Node;
        return (ns, parentType, returnType, method.Name, value, keyIndex, parameters.ToImmutable(),
            TypeModifiers(method.ContainingType), syntax.Modifiers.ToString());

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
            KeyIndex, ImmutableArray<(string Type, string Name)> Parameters, string TypeModifiers, string
            MethodModifiers)> methods)
    {
        if (methods.IsDefaultOrEmpty) return;

        var sb = new StringBuilder("// <auto-generated />")
            .AppendLine().Append("// ").Append(GetType().Name).Append(" v").Append(GetVersion()).AppendLine();

        bool first;
        int indent = 0;

        // find the unique param types, so we can build helpers
        Dictionary<(ImmutableArray<(string Type, string Name)> Parameters, int KeyIndex), string> formatters = new(FormatterComparer.Default);
        foreach (var method in methods)
        {
            switch (method.Parameters.Length)
            {
                case 0: continue;
                case 1:
                    if (method.Parameters[0].Type is "string" or "int" or "long" or "float" or "double") continue;
                    break;
            }

            var key = (method.Parameters, method.KeyIndex);
            if (!formatters.ContainsKey(key))
            {
                formatters.Add(key, $"__RespFormatter_{formatters.Count}");
            }
        }

        StringBuilder NewLine() => sb.AppendLine().Append(' ', indent * 4);
        NewLine().Append("using System;");
        NewLine().Append("#pragma warning disable CS8981");
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
                string? formatter = formatters.TryGetValue((method.Parameters, method.KeyIndex), out var tmp) ? tmp : null;

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

                    sb.Append(param.Type).Append(' ').Append(param.Name);
                }

                sb.Append(")");
                indent++;
                sb = NewLine().Append("=> RespMessage.Create(").Append(csValue).Append("u8, ");
                switch (method.Parameters.Length)
                {
                    case 0:
                        sb.Append("default(Resp.Core.Void)");
                        break;
                    default:
                        WriteTuple(method.Parameters, sb, TupleMode.Values);
                        break;
                }

                if (formatter is not null)
                {
                    sb.Append(", ").Append(formatter).Append(".Default");
                }
                sb.Append(").Wait");
                if (!string.IsNullOrWhiteSpace(method.ReturnType))
                {
                    sb.Append('<').Append(method.ReturnType).Append('>');
                }
                sb.Append("(_connection, timeout: _timeout);");
                indent--;
                NewLine();

                sb = NewLine().Append(RemovePartial(method.MethodModifiers)).Append(" System.Threading.Tasks.Task");
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

                    sb.Append(param.Type).Append(' ').Append(param.Name);
                }

                sb.Append(")");
                indent++;
                sb = NewLine().Append("=> RespMessage.Create(").Append(csValue).Append("u8, ");
                switch (method.Parameters.Length)
                {
                    case 0:
                        sb.Append("default(Resp.Core.Void)");
                        break;
                    default:
                        WriteTuple(method.Parameters, sb, TupleMode.Values);
                        break;
                }

                if (formatter is not null)
                {
                    sb.Append(", ").Append(formatter).Append(".Default");
                }
                sb.Append(").WaitAsync");
                if (!string.IsNullOrWhiteSpace(method.ReturnType))
                {
                    sb.Append('<').Append(method.ReturnType).Append('>');
                }
                sb.Append("(_connection, cancellationToken: _cancellationToken);");
                indent--;
                NewLine();
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                indent--;
                NewLine().Append("}");
            }
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

            sb = NewLine().Append("public void Format(scoped ReadOnlySpan<byte> command, ref Resp.RespWriter writer, in ");
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

        ctx.AddSource(GetType().Name + ".generated.cs", sb.ToString());

        static void WriteTuple(ImmutableArray<(string Type, string Name)> parameters, StringBuilder sb, TupleMode mode)
        {
            if (parameters.IsDefaultOrEmpty) return;
            if (parameters.Length == 1)
            {
                sb.Append(mode == TupleMode.Values ? parameters[0].Name : parameters[0].Type);
                return;
            }
            sb.Append('(');
            int index = 0;
            foreach (var param in parameters)
            {
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
internal sealed class FormatterComparer : IEqualityComparer<(ImmutableArray<(string Type, string Name)> Parameters, int KeyIndex)>
{
    private FormatterComparer() { }
    public static readonly FormatterComparer Default = new();

    public bool Equals(
        (ImmutableArray<(string Type, string Name)> Parameters, int KeyIndex) x,
        (ImmutableArray<(string Type, string Name)> Parameters, int KeyIndex) y)
    {
        if (x.KeyIndex != y.KeyIndex) return false;
        if (x.Parameters.Length != y.Parameters.Length) return false;
        for (int i = 0; i < x.Parameters.Length; i++)
        {
            if (x.Parameters[i].Type != y.Parameters[i].Type) return false;
        }

        return true;
    }

    public int GetHashCode((ImmutableArray<(string Type, string Name)> Parameters, int KeyIndex) obj)
    {
        var hash = obj.KeyIndex & obj.Parameters.Length;
        foreach (var p in obj.Parameters)
        {
            hash ^= p.Type.GetHashCode();
        }

        return hash;
    }
}
