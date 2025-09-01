using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
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

    private static string GetFullName(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string GetName(ITypeSymbol type)
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

    private (string Namespace, string TypeName, string ReturnType, string MethodName, string Command,
        ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> Parameters, string
        TypeModifiers, string
        MethodModifiers, string Context, string? Formatter, string? Parser) Transform(
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
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        else if (method.ReturnType is null)
        {
            return default;
        }
        else
        {
            returnType = GetFullName(method.ReturnType);
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
        string? formatter = null, parser = null;
        foreach (var attrib in method.GetAttributes())
        {
            if (attrib.AttributeClass is
                {
                    Name: "RespCommandAttribute",
                    ContainingNamespace: { Name: "RESPite", ContainingNamespace.IsGlobalNamespace: true }
                })
            {
                if (attrib.ConstructorArguments.Length == 1)
                {
                    if (attrib.ConstructorArguments[0].Value?.ToString() is { Length: > 0 } val)
                    {
                        value = val;
                        break;
                    }

                    foreach (var tuple in attrib.NamedArguments)
                    {
                        switch (tuple.Key)
                        {
                            case "Formatter":
                                formatter = tuple.Value.Value?.ToString();
                                break;
                            case "Parser":
                                parser = tuple.Value.Value?.ToString();
                                break;
                        }
                    }
                }
            }
        }

        var parameters =
            ImmutableArray.CreateBuilder<(string Type, string Name, string Modifiers, ParameterFlags Flags)>(
                method.Parameters.Length);

        // get context from the available fields
        string? context = null;

        foreach (var param in method.Parameters)
        {
            if (IsRespContext(param.Type))
            {
                context = param.Name;
                break;
            }
        }

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

        static bool Ignore(ITypeSymbol symbol) => IsRespContext(symbol); // CT etc?

        foreach (var param in method.Parameters)
        {
            var flags = ParameterFlags.Parameter;
            if (IsKey(param)) flags |= ParameterFlags.Key;
            if (!Ignore(param.Type))
            {
                flags |= ParameterFlags.Data;
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
            }

            parameters.Add((GetFullName(param.Type), param.Name, modifiers, flags));
        }

        static bool IsRespContext(ITypeSymbol type)
            => type is INamedTypeSymbol
            {
                Name: "RespContext",
                ContainingNamespace: { Name: "RESPite", ContainingNamespace.IsGlobalNamespace: true }
            };

        var syntax = (MethodDeclarationSyntax)ctx.Node;
        return (ns, parentType, returnType, method.Name, value, parameters.ToImmutable(),
            TypeModifiers(method.ContainingType), syntax.Modifiers.ToString(), context ?? "", formatter, parser);

        static string TypeModifiers(ITypeSymbol type)
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
        if (param.Name.EndsWith("key", StringComparison.InvariantCultureIgnoreCase))
        {
            return true;
        }

        foreach (var attrib in param.GetAttributes())
        {
            if (attrib.AttributeClass is
                {
                    Name: "KeyAttribute",
                    ContainingNamespace: { Name: "RESPite", ContainingNamespace.IsGlobalNamespace: true }
                })
            {
                return true;
            }
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
        ImmutableArray<(string Namespace, string TypeName, string ReturnType, string MethodName, string Command,
            ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> Parameters, string
            TypeModifiers,
            string
            MethodModifiers, string Context, string? Formatter, string? Parser)> methods)
    {
        if (methods.IsDefaultOrEmpty) return;

        var sb = new StringBuilder("// <auto-generated />")
            .AppendLine().Append("// ").Append(GetType().Name).Append(" v").Append(GetVersion()).AppendLine();

        bool first;
        int indent = 0;

        // find the unique param types, so we can build helpers
        Dictionary<ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)>, (string Name,
                bool Shared)>
            formatters =
                new(FormatterComparer.Default);
        static bool IsThis(string modifier) => modifier.StartsWith("this ");

        foreach (var method in methods)
        {
            if (method.Formatter is not null || DataParameterCount(method.Parameters) < 2)
            {
                continue; // consumer should add their own extension method for the target type
            }

            var key = method.Parameters;
            if (!formatters.TryGetValue(key, out var existing))
            {
                formatters.Add(key, ($"__RespFormatter_{formatters.Count}", false));
            }
            else if (!existing.Shared)
            {
                formatters[key] = (existing.Name, true); // mark shared
            }
        }

        StringBuilder NewLine() => sb.AppendLine().Append(' ', Math.Max(indent * 4, 0));
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
                bool isSharedFormatter = false;
                string? formatter = method.Formatter
                                    ?? InbuiltFormatter(method.Parameters);
                if (formatter is null && formatters.TryGetValue(method.Parameters, out var tmp))
                {
                    formatter = $"{tmp.Name}.Default";
                    isSharedFormatter = tmp.Shared;
                }

                // perform string escaping on the generated value (this includes the quotes, note)
                var csValue = SyntaxFactory
                    .LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(method.Command))
                    .ToFullString();

                WriteMethod(false);
                WriteMethod(true);

                void WriteMethod(bool asAsync)
                {
                    sb = NewLine().Append(asAsync ? RemovePartial(method.MethodModifiers) : method.MethodModifiers)
                        .Append(' ');
                    if (asAsync)
                    {
                        sb.Append("ValueTask");
                        if (!string.IsNullOrWhiteSpace(method.ReturnType))
                        {
                            sb.Append('<').Append(method.ReturnType).Append('>');
                        }
                    }
                    else
                    {
                        sb.Append(string.IsNullOrEmpty(method.ReturnType) ? "void" : method.ReturnType);
                    }

                    sb.Append(' ').Append(method.MethodName).Append(asAsync ? "Async" : "").Append("(");
                    first = true;
                    foreach (var param in method.Parameters)
                    {
                        if ((param.Flags & ParameterFlags.Parameter) == 0) continue;
                        if (!first) sb.Append(", ");
                        first = false;

                        sb.Append(param.Modifiers).Append(param.Type).Append(' ').Append(param.Name);
                    }

                    var dataParameters = DataParameterCount(method.Parameters);
                    sb.Append(")");
                    indent++;

                    var parser = method.Parser ?? InbuiltParser(method.ReturnType, explicitSuccess: true);
                    bool useDirectCall = method.Context is { Length: > 0 } & formatter is { Length: > 0 } &
                                         parser is { Length: > 0 };

                    if (string.IsNullOrWhiteSpace(method.Context))
                    {
                        NewLine().Append("=> throw new NotSupportedException(\"No RespContext available\");");
                        useDirectCall = false;
                    }
                    else if (!(useDirectCall & asAsync))
                    {
                        sb = NewLine();
                        if (useDirectCall) sb.Append("// ");
                        sb.Append("=> ").Append(method.Context).Append(".Command(").Append(csValue).Append("u8");
                        if (dataParameters != 0)
                        {
                            sb.Append(", ");
                            WriteTuple(method.Parameters, sb, TupleMode.Values);

                            if (!string.IsNullOrWhiteSpace(formatter))
                            {
                                sb.Append(", ").Append(formatter);
                            }
                        }

                        sb.Append(asAsync ? ").Send" : ").Wait");
                        if (!string.IsNullOrWhiteSpace(method.ReturnType))
                        {
                            sb.Append('<').Append(method.ReturnType).Append('>');
                        }

                        sb.Append("(").Append(parser).Append(");");
                    }

                    if (useDirectCall) // avoid the intermediate step when possible
                    {
                        sb = NewLine().Append("=> ").Append(method.Context).Append(".Send")
                            .Append('<');
                        WriteTuple(
                            method.Parameters,
                            sb,
                            isSharedFormatter ? TupleMode.SyntheticNames : TupleMode.NamedTuple);
                        if (!string.IsNullOrWhiteSpace(method.ReturnType))
                        {
                            sb.Append(", ").Append(method.ReturnType);
                        }
                        sb.Append(">(").Append(csValue).Append("u8").Append(", ");
                        WriteTuple(method.Parameters, sb, TupleMode.Values);
                        sb.Append(", ").Append(formatter).Append(", ").Append(parser).Append(")");
                        if (asAsync)
                        {
                            sb.Append(".AsValueTask()");
                        }
                        else
                        {
                            sb.Append(".Wait(").Append(method.Context).Append(".SyncTimeout)");
                        }

                        sb.Append(";");
                    }

                    indent--;
                    NewLine();
                }
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
            var parameters = tuple.Key;
            var name = tuple.Value.Name;
            var names = tuple.Value.Shared ? TupleMode.SyntheticNames : TupleMode.NamedTuple;

            NewLine();
            sb = NewLine().Append("sealed file class ").Append(name).Append(" : global::RESPite.Messages.IRespFormatter<");
            WriteTuple(parameters, sb, names);
            sb.Append('>');
            NewLine().Append("{");
            indent++;
            NewLine().Append("public static readonly ").Append(name).Append(" Default = new();");
            NewLine();

            sb = NewLine()
                .Append("public void Format(scoped ReadOnlySpan<byte> command, ref global::RESPite.Messages.RespWriter writer, in ");
            WriteTuple(parameters, sb, names);
            sb.Append(" request)");
            NewLine().Append("{");
            indent++;
            var count = DataParameterCount(parameters);
            sb = NewLine().Append("writer.WriteCommand(command, ").Append(count);
            sb.Append(");");
            if (count == 1)
            {
                NewLine().Append("writer.WriteBulkString(request);");
            }
            else
            {
                int index = 0;
                foreach (var parameter in parameters)
                {
                    if ((parameter.Flags & ParameterFlags.DataParameter) == ParameterFlags.DataParameter)
                    {
                        sb = NewLine().Append("writer.")
                            .Append((parameter.Flags & ParameterFlags.Key) == 0 ? "WriteBulkString" : "WriteKey")
                            .Append("(request.");
                        if (names == TupleMode.SyntheticNames)
                        {
                            sb.Append("Arg").Append(index);
                        }
                        else
                        {
                            sb.Append(parameter.Name);
                        }

                        sb.Append(");");
                        index++;
                    }
                }

                Debug.Assert(index == count, "wrote all parameters");
            }

            indent--;
            NewLine().Append("}");
            indent--;
            NewLine().Append("}");
        }

        NewLine();
        ctx.AddSource(GetType().Name + ".generated.cs", sb.ToString());

        static void WriteTuple(
            ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> parameters,
            StringBuilder sb,
            TupleMode mode)
        {
            var count = DataParameterCount(parameters);
            if (count == 0) return;
            if (count < 2)
            {
                var p = FirstDataParameter(parameters);
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
                if ((param.Flags & ParameterFlags.DataParameter) != ParameterFlags.DataParameter)
                {
                    continue; // note don't increase index
                }

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

    private static string? InbuiltFormatter(
        ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> parameters)
    {
        if (DataParameterCount(parameters) == 1)
        {
            var p = FirstDataParameter(parameters);
            return InbuiltFormatter(p.Type, (p.Flags & ParameterFlags.Key) != 0);
        }

        return null;
    }

    private static (string Type, string Name, string Modifiers, ParameterFlags Flags) FirstDataParameter(
        ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> parameters)
    {
        if (!parameters.IsDefaultOrEmpty)
        {
            foreach (var parameter in parameters)
            {
                if ((parameter.Flags & ParameterFlags.DataParameter) == ParameterFlags.DataParameter)
                {
                    return parameter;
                }
            }
        }

        return Array.Empty<(string Type, string Name, string Modifiers, ParameterFlags Flags)>().First();
    }

    private static int DataParameterCount(
        ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> parameters)
    {
        if (parameters.IsDefaultOrEmpty) return 0;
        int count = 0;
        foreach (var parameter in parameters)
        {
            if ((parameter.Flags & ParameterFlags.DataParameter) == ParameterFlags.DataParameter)
            {
                count++;
            }
        }

        return count;
    }

    private const string RespFormattersPrefix = "global::RESPite.RespFormatters.";

    private static string? InbuiltFormatter(string type, bool isKey) => type switch
    {
        "string" => isKey ? (RespFormattersPrefix + "Key.String") : (RespFormattersPrefix + "Value.String"),
        "byte[]" => isKey ? (RespFormattersPrefix + "Key.ByteArray") : (RespFormattersPrefix + "Value.ByteArray"),
        "int" => RespFormattersPrefix + "Int32",
        "long" => RespFormattersPrefix + "Int64",
        "float" => RespFormattersPrefix + "Single",
        "double" => RespFormattersPrefix + "Double",
        _ => null,
    };

    private const string RespParsersPrefix = "global::RESPite.RespParsers.";

    private static string? InbuiltParser(string type, bool explicitSuccess = false) => type switch
    {
        "" when explicitSuccess => RespParsersPrefix + "Success",
        "bool" => RespParsersPrefix + "Success",
        "string" => RespParsersPrefix + "String",
        "int" => RespParsersPrefix + "Int32",
        "long" => RespParsersPrefix + "Int64",
        "float" => RespParsersPrefix + "Single",
        "double" => RespParsersPrefix + "Double",
        "int?" => RespParsersPrefix + "NullableInt32",
        "long?" => RespParsersPrefix + "NullableInt64",
        "float?" => RespParsersPrefix + "NullableSingle",
        "double?" => RespParsersPrefix + "NullableDouble",
        "global::RESPite.RespParsers.ResponseSummary" => RespParsersPrefix + "ResponseSummary.Parser",
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

    [Flags]
    private enum ParameterFlags
    {
        None = 0,
        Parameter = 1 << 0,
        Data = 1 << 1,
        DataParameter = Data | Parameter,
        Key = 1 << 2,
        Literal = 1 << 3,
    }

    // compares whether a formatter can be shared, which depends on the key index and types (not names)
    private sealed class
        FormatterComparer
        : IEqualityComparer<ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)>>
    {
        private FormatterComparer() { }
        public static readonly FormatterComparer Default = new();

        public bool Equals(
            ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> x,
            ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> y)
        {
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
            {
                var px = x[i];
                var py = y[i];
                if (px.Type != py.Type || px.Flags != py.Flags) return false;
                // literals need to match by name too
                if ((px.Flags & ParameterFlags.Literal) != 0
                    && px.Name != py.Name) return false;
            }

            return true;
        }

        public int GetHashCode(
            ImmutableArray<(string Type, string Name, string Modifiers, ParameterFlags Flags)> obj)
        {
            var hash = obj.Length;
            foreach (var p in obj)
            {
                hash ^= p.Type.GetHashCode() ^ (int)p.Flags;
            }

            return hash;
        }
    }
}
