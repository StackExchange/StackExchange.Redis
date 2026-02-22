using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RESPite;

namespace StackExchange.Redis.Build;

[Generator(LanguageNames.CSharp)]
public class AsciiHashGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // looking for [AsciiHash] partial static class Foo { }
        var types = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax decl && IsStaticPartial(decl.Modifiers) &&
                                    HasAsciiHash(decl.AttributeLists),
                TransformTypes)
            .Where(pair => pair.Name is { Length: > 0 })
            .Collect();

        // looking for [AsciiHash] partial static bool TryParse(input, out output) { }
        var methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax decl && IsStaticPartial(decl.Modifiers) &&
                                    HasAsciiHash(decl.AttributeLists),
                TransformMethods)
            .Where(pair => pair.Name is { Length: > 0 })
            .Collect();

        context.RegisterSourceOutput(types.Combine(methods), Generate);

        static bool IsStaticPartial(SyntaxTokenList tokens)
            => tokens.Any(SyntaxKind.StaticKeyword) && tokens.Any(SyntaxKind.PartialKeyword);

        static bool HasAsciiHash(SyntaxList<AttributeListSyntax> attributeLists)
        {
            foreach (var attribList in attributeLists)
            {
                foreach (var attrib in attribList.Attributes)
                {
                    if (attrib.Name.ToString() is nameof(AsciiHashAttribute) or nameof(AsciiHash)) return true;
                }
            }

            return false;
        }
    }

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

    private static AttributeData? TryGetAsciiHashAttribute(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attrib in attributes)
        {
            if (attrib.AttributeClass is
                {
                    Name: nameof(AsciiHashAttribute),
                    ContainingType: null,
                    ContainingNamespace:
                    {
                        Name: "RESPite",
                        ContainingNamespace.IsGlobalNamespace: true,
                    }
                })
            {
                return attrib;
            }
        }

        return null;
    }

    private (string Namespace, string ParentType, string Name, string Value) TransformTypes(
        GeneratorSyntaxContext ctx,
        CancellationToken cancellationToken)
    {
        // extract the name and value (defaults to name, but can be overridden via attribute) and the location
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol named) return default;
        if (TryGetAsciiHashAttribute(named.GetAttributes()) is not { } attrib) return default;

        string ns = "", parentType = "";
        if (named.ContainingType is { } containingType)
        {
            parentType = GetName(containingType);
            ns = containingType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }
        else if (named.ContainingNamespace is { } containingNamespace)
        {
            ns = containingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        string name = named.Name, value = GetRawValue(name, attrib);

        return (ns, parentType, name, value);
    }

    private static string GetRawValue(string name, AttributeData? asciiHashAttribute)
    {
        var value = "";
        if (asciiHashAttribute is { ConstructorArguments.Length: 1 }
            && asciiHashAttribute.ConstructorArguments[0].Value?.ToString() is { Length: > 0 } val)
        {
            value = val;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            value = InferPayload(name); // if nothing explicit: infer from name
        }

        return value;
    }

    private static string InferPayload(string name) => name.Replace("_", "-");

    private (string Namespace, string ParentType, Accessibility Accessibility, string Name,
        (string Type, string Name, bool IsBytes, RefKind RefKind) From, (string Type, string Name, RefKind RefKind) To,
        (string Name, bool Value, RefKind RefKind) CaseSensitive,
        BasicArray<(string EnumMember, string ParseText)> Members, int DefaultValue) TransformMethods(
            GeneratorSyntaxContext ctx,
            CancellationToken cancellationToken)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not IMethodSymbol
            {
                IsStatic: true,
                IsPartialDefinition: true,
                PartialImplementationPart: null,
                Arity: 0,
                ReturnType.SpecialType: SpecialType.System_Boolean,
                Parameters:
                {
                    IsDefaultOrEmpty: false,
                    Length: 2 or 3,
                },
            } method) return default;

        if (TryGetAsciiHashAttribute(method.GetAttributes()) is not { } attrib) return default;

        if (method.ContainingType is not { } containingType) return default;
        var parentType = GetName(containingType);
        var ns = containingType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        var arg = method.Parameters[0];
        if (arg is not { IsOptional: false, RefKind: RefKind.None or RefKind.In or RefKind.Ref or RefKind.RefReadOnlyParameter }) return default;
        var fromType = arg.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        bool fromBytes = fromType is "byte[]" || fromType.EndsWith("Span<byte>");
        var from = (fromType, arg.Name, fromBytes, arg.RefKind);

        arg = method.Parameters[1];
        if (arg is not
            {
                IsOptional: false, RefKind: RefKind.Out or RefKind.Ref, Type: INamedTypeSymbol { TypeKind: TypeKind.Enum }
            }) return default;
        var to = (arg.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), arg.Name, arg.RefKind);

        var members = arg.Type.GetMembers();
        var builder = new BasicArray<(string EnumMember, string ParseText)>.Builder(members.Length);
        HashSet<int> values = new();
        foreach (var member in members)
        {
            if (member is IFieldSymbol { IsStatic: true, IsConst: true } field)
            {
                var rawValue = GetRawValue(field.Name, TryGetAsciiHashAttribute(member.GetAttributes()));
                builder.Add((field.Name, rawValue));
                int value = field.ConstantValue switch
                {
                    sbyte i8 => i8,
                    short i16 => i16,
                    int i32 => i32,
                    long i64 => (int)i64,
                    byte u8 => u8,
                    ushort u16 => u16,
                    uint u32 => (int)u32,
                    ulong u64 => (int)u64,
                    char c16 => c16,
                    _ => 0,
                };
                values.Add(value);
            }
        }

        (string, bool, RefKind) caseSensitive;
        bool cs = IsCaseSensitive(attrib);
        if (method.Parameters.Length > 2)
        {
            arg = method.Parameters[2];
            if (arg is not
                {
                    RefKind: RefKind.None or RefKind.In or RefKind.Ref or RefKind.RefReadOnlyParameter,
                    Type.SpecialType: SpecialType.System_Boolean,
                })
            {
                return default;
            }

            if (arg.IsOptional)
            {
                if (arg.ExplicitDefaultValue is not bool dv) return default;
                cs = dv;
            }
            caseSensitive = (arg.Name, cs, arg.RefKind);
        }
        else
        {
            caseSensitive = ("", cs, RefKind.None);
        }

        int defaultValue = 0;
        if (values.Contains(0))
        {
            int len = values.Count;
            for (int i = 1; i <= len; i++)
            {
                if (!values.Contains(i))
                {
                    defaultValue = i;
                    break;
                }
            }
        }
        return (ns, parentType, method.DeclaredAccessibility, method.Name, from, to, caseSensitive, builder.Build(), defaultValue);
    }

    private bool IsCaseSensitive(AttributeData attrib)
    {
        foreach (var member in attrib.NamedArguments)
        {
            if (member.Key == nameof(AsciiHashAttribute.CaseSensitive)
                && member.Value.Kind is TypedConstantKind.Primitive
                && member.Value.Value is bool caseSensitive)
            {
                return caseSensitive;
            }
        }

        return true;
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
        (ImmutableArray<(string Namespace, string ParentType, string Name, string Value)> Types, ImmutableArray<(string
            Namespace, string ParentType, Accessibility Accessibility, string Name,
            (string Type, string Name, bool IsBytes, RefKind RefKind) From, (string Type, string Name, RefKind RefKind) To,
            (string Name, bool Value, RefKind RefKind) CaseSensitive,
            BasicArray<(string EnumMember, string ParseText)> Members, int DefaultValue)> Enums) content)
    {
        var types = content.Types;
        var enums = content.Enums;
        if (types.IsDefaultOrEmpty & enums.IsDefaultOrEmpty) return; // nothing to do

        var sb = new StringBuilder("// <auto-generated />")
            .AppendLine().Append("// ").Append(GetType().Name).Append(" v").Append(GetVersion()).AppendLine();

        sb.AppendLine("using System;");
        sb.AppendLine("using StackExchange.Redis;");
        sb.AppendLine("#pragma warning disable CS8981");

        BuildTypeImplementations(sb, types);
        BuildEnumParsers(sb, enums);
        ctx.AddSource(nameof(AsciiHash) + ".generated.cs", sb.ToString());
    }

    private void BuildEnumParsers(
        StringBuilder sb,
        in ImmutableArray<(string Namespace, string ParentType, Accessibility Accessibility, string Name,
            (string Type, string Name, bool IsBytes, RefKind RefKind) From,
            (string Type, string Name, RefKind RefKind) To,
            (string Name, bool Value, RefKind RefKind) CaseSensitive,
            BasicArray<(string EnumMember, string ParseText)> Members, int DefaultValue)> enums)
    {
        if (enums.IsDefaultOrEmpty) return; // nope

        int indent = 0;
        StringBuilder NewLine() => sb.AppendLine().Append(' ', indent * 4);

        foreach (var grp in enums.GroupBy(l => (l.Namespace, l.ParentType)))
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

            if (!string.IsNullOrWhiteSpace(grp.Key.ParentType))
            {
                if (grp.Key.ParentType.Contains('.')) // nested types
                {
                    foreach (var part in grp.Key.ParentType.Split('.'))
                    {
                        NewLine().Append("partial class ").Append(part);
                        NewLine().Append("{");
                        indent++;
                        braces++;
                    }
                }
                else
                {
                    NewLine().Append("partial class ").Append(grp.Key.ParentType);
                    NewLine().Append("{");
                    indent++;
                    braces++;
                }
            }

            foreach (var method in grp)
            {
                var line = NewLine().Append(Format(method.Accessibility)).Append(" static partial bool ")
                    .Append(method.Name).Append("(")
                    .Append(Format(method.From.RefKind))
                    .Append(method.From.Type).Append(" ").Append(method.From.Name).Append(", ")
                    .Append(Format(method.To.RefKind))
                    .Append(method.To.Type).Append(" ").Append(method.To.Name);
                if (!string.IsNullOrEmpty(method.CaseSensitive.Name))
                {
                    line.Append(", ").Append(Format(method.CaseSensitive.RefKind)).Append("bool ")
                        .Append(method.CaseSensitive.Name);
                }
                line.Append(")");
                NewLine().Append("{");
                indent++;
                NewLine().Append("// ").Append(method.To.Type).Append(" has ").Append(method.Members.Length).Append(" members");
                string valueTarget = method.To.Name;
                if (method.To.RefKind != RefKind.Out)
                {
                    valueTarget = "__tmp";
                    NewLine().Append(method.To.Type).Append(" ").Append(valueTarget).Append(";");
                }
                if (string.IsNullOrEmpty(method.CaseSensitive.Name))
                {
                    Write(method.CaseSensitive.Value);
                }
                else
                {
                    NewLine().Append("if (").Append(method.CaseSensitive.Name).Append(")");
                    NewLine().Append("{");
                    indent++;
                    Write(true);
                    indent--;
                    NewLine().Append("}");
                    NewLine().Append("else");
                    NewLine().Append("{");
                    indent++;
                    Write(false);
                    indent--;
                    NewLine().Append("}");
                }

                if (method.To.RefKind == RefKind.Out)
                {
                    NewLine().Append("return ").Append(method.To.Name).Append(" != (")
                        .Append(method.To.Type).Append(")").Append(method.DefaultValue).Append(";");
                }
                else
                {
                    NewLine().Append("// do not update parameter on miss");
                    NewLine().Append("if (").Append(valueTarget).Append(" == (")
                        .Append(method.To.Type).Append(")").Append(method.DefaultValue).Append(") return false;");
                    NewLine().Append(method.To.Name).Append(" = ").Append(valueTarget).Append(";");
                    NewLine().Append("return true;");
                }

                void Write(bool caseSensitive)
                {
                    NewLine().Append("var hash = global::RESPite.AsciiHash.")
                        .Append(caseSensitive ? nameof(AsciiHash.HashCS) : nameof(AsciiHash.HashCI)).Append("(").Append(method.From.Name)
                        .Append(");");
                    NewLine().Append(valueTarget).Append(" = ").Append(method.From.Name).Append(".Length switch {");
                    indent++;
                    foreach (var member in method.Members.OrderBy(x => x.ParseText.Length))
                    {
                        var len = member.ParseText.Length;
                        var line = NewLine().Append(len).Append(" when hash is ")
                            .Append(caseSensitive ? AsciiHash.HashCS(member.ParseText) : AsciiHash.HashCI(member.ParseText));

                        if (!(len <= AsciiHash.MaxBytesHashIsEqualityCS & caseSensitive))
                        {
                            // check the value
                            var csValue = SyntaxFactory
                                .LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    SyntaxFactory.Literal(member.ParseText))
                                .ToFullString();

                            line.Append(" && global::RESPite.AsciiHash.")
                                .Append(caseSensitive ? nameof(AsciiHash.SequenceEqualsCS) : nameof(AsciiHash.SequenceEqualsCI))
                                .Append("(").Append(method.From.Name).Append(", ").Append(csValue);
                            if (method.From.IsBytes) line.Append("u8");
                            line.Append(")");
                        }

                        line.Append(" => ").Append(method.To.Type).Append(".").Append(member.EnumMember).Append(",");
                    }

                    NewLine().Append("_ => (").Append(method.To.Type).Append(")").Append(method.DefaultValue)
                        .Append(",");
                    indent--;
                    NewLine().Append("};");
                }

                indent--;
                NewLine().Append("}");
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                indent--;
                NewLine().Append("}");
            }
        }
    }

    private static string Format(RefKind refKind) => refKind switch
    {
        RefKind.None => "",
        RefKind.In => "in ",
        RefKind.Out => "out ",
        RefKind.Ref => "ref ",
        RefKind.RefReadOnlyParameter or RefKind.RefReadOnly => "ref readonly ",
        _ => throw new NotSupportedException($"RefKind {refKind} is not yet supported."),
    };
    private static string Format(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Private => "private",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        _ => throw new NotSupportedException($"Accessibility {accessibility} is not yet supported."),
    };

    private static void BuildTypeImplementations(
        StringBuilder sb,
        in ImmutableArray<(string Namespace, string ParentType, string Name, string Value)> types)
    {
        if (types.IsDefaultOrEmpty) return; // nope

        int indent = 0;
        StringBuilder NewLine() => sb.AppendLine().Append(' ', indent * 4);

        foreach (var grp in types.GroupBy(l => (l.Namespace, l.ParentType)))
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

            if (!string.IsNullOrWhiteSpace(grp.Key.ParentType))
            {
                if (grp.Key.ParentType.Contains('.')) // nested types
                {
                    foreach (var part in grp.Key.ParentType.Split('.'))
                    {
                        NewLine().Append("partial class ").Append(part);
                        NewLine().Append("{");
                        indent++;
                        braces++;
                    }
                }
                else
                {
                    NewLine().Append("partial class ").Append(grp.Key.ParentType);
                    NewLine().Append("{");
                    indent++;
                    braces++;
                }
            }

            foreach (var literal in grp)
            {
                // perform string escaping on the generated value (this includes the quotes, note)
                var csValue = SyntaxFactory
                    .LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(literal.Value))
                    .ToFullString();

                var hashCS = AsciiHash.HashCS(literal.Value);
                var hashCI = AsciiHash.HashCI(literal.Value);
                NewLine().Append("static partial class ").Append(literal.Name);
                NewLine().Append("{");
                indent++;
                NewLine().Append("public const int Length = ").Append(literal.Value.Length).Append(';');
                NewLine().Append("public const long HashCS = ").Append(hashCS).Append(';');
                NewLine().Append("public const long HashCI = ").Append(hashCI).Append(';');
                NewLine().Append("public static ReadOnlySpan<byte> U8 => ").Append(csValue).Append("u8;");
                NewLine().Append("public const string Text = ").Append(csValue).Append(';');
                if (literal.Value.Length <= AsciiHash.MaxBytesHashIsEqualityCS)
                {
                    // the case-sensitive hash enforces all the values
                    NewLine().Append(
                        "public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;");
                    NewLine().Append(
                        "public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.AsciiHash.HashCS(value) == HashCS || global::RESPite.AsciiHash.EqualsCI(value, U8));");
                }
                else
                {
                    NewLine().Append(
                        "public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);");
                    NewLine().Append(
                        "public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.AsciiHash.EqualsCI(value, U8);");
                }

                indent--;
                NewLine().Append("}");
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                indent--;
                NewLine().Append("}");
            }
        }
    }
}
