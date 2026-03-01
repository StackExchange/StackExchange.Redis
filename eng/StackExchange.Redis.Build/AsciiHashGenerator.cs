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

        // looking for [AsciiHash("some type")] enum Foo { }
        var enums = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is EnumDeclarationSyntax decl && HasAsciiHash(decl.AttributeLists),
                TransformEnums)
            .Where(pair => pair.Name is { Length: > 0 })
            .Collect();

        context.RegisterSourceOutput(
            types.Combine(methods).Combine(enums),
            (ctx, content) =>
                Generate(ctx, content.Left.Left, content.Left.Right, content.Right));

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

    private (string Namespace, string ParentType, string Name, int Count, int MaxChars, int MaxBytes) TransformEnums(
        GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
    {
        // extract the name and value (defaults to name, but can be overridden via attribute) and the location
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol { TypeKind: TypeKind.Enum } named) return default;
        if (TryGetAsciiHashAttribute(named.GetAttributes()) is not { } attrib) return default;
        var innerName = GetRawValue("", attrib);
        if (string.IsNullOrWhiteSpace(innerName)) return default;

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

        int maxChars = 0, maxBytes = 0, count = 0;
        foreach (var member in named.GetMembers())
        {
            if (member.Kind is SymbolKind.Field)
            {
                var rawValue = GetRawValue(member.Name, TryGetAsciiHashAttribute(member.GetAttributes()));
                if (string.IsNullOrWhiteSpace(rawValue)) continue;

                count++;
                maxChars = Math.Max(maxChars, rawValue.Length);
                maxBytes = Math.Max(maxBytes, Encoding.UTF8.GetByteCount(rawValue));
            }
        }
        return (ns, parentType, innerName, count, maxChars, maxBytes);
    }

    private (string Namespace, string ParentType, string Name, string Value) TransformTypes(
        GeneratorSyntaxContext ctx,
        CancellationToken cancellationToken)
    {
        // extract the name and value (defaults to name, but can be overridden via attribute) and the location
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol { TypeKind: TypeKind.Class } named) return default;
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
        if (string.IsNullOrWhiteSpace(value)) return default;
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
                if (string.IsNullOrWhiteSpace(rawValue)) continue;
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
        ImmutableArray<(string Namespace, string ParentType, string Name, string Value)> types,
        ImmutableArray<(string Namespace, string ParentType, Accessibility Accessibility, string Name,
            (string Type, string Name, bool IsBytes, RefKind RefKind) From, (string Type, string Name, RefKind RefKind) To,
            (string Name, bool Value, RefKind RefKind) CaseSensitive,
            BasicArray<(string EnumMember, string ParseText)> Members, int DefaultValue)> parseMethods,
        ImmutableArray<(string Namespace, string ParentType, string Name, int Count, int MaxChars, int MaxBytes)> enums)
    {
        if (types.IsDefaultOrEmpty & parseMethods.IsDefaultOrEmpty & enums.IsDefaultOrEmpty) return; // nothing to do

        var sb = new StringBuilder("// <auto-generated />")
            .AppendLine().Append("// ").Append(GetType().Name).Append(" v").Append(GetVersion()).AppendLine();

        sb.AppendLine("using System;");
        sb.AppendLine("using StackExchange.Redis;");
        sb.AppendLine("#pragma warning disable CS8981, SER004");

        BuildTypeImplementations(sb, types);
        BuildEnumParsers(sb, parseMethods);
        BuildEnumLengths(sb, enums);
        ctx.AddSource(nameof(AsciiHash) + ".generated.cs", sb.ToString());
    }

    private void BuildEnumLengths(StringBuilder sb, ImmutableArray<(string Namespace, string ParentType, string Name, int Count, int MaxChars, int MaxBytes)> enums)
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

            foreach (var @enum in grp)
            {
                NewLine().Append("internal static partial class ").Append(@enum.Name);
                NewLine().Append("{");
                indent++;
                NewLine().Append("public const int EnumCount = ").Append(@enum.Count).Append(";");
                NewLine().Append("public const int MaxChars = ").Append(@enum.MaxChars).Append(";");
                NewLine().Append("public const int MaxBytes = ").Append(@enum.MaxBytes).Append("; // as UTF8");
                // for buffer bytes: we want to allow 1 extra byte (to check for false-positive over-long values),
                // and then round up to the nearest multiple of 8 (for stackalloc performance, etc)
                int bufferBytes = (@enum.MaxBytes + 1 + 7) & ~7;
                NewLine().Append("public const int BufferBytes = ").Append(bufferBytes).Append(";");
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

                bool alwaysCaseSensitive =
                    string.IsNullOrEmpty(method.CaseSensitive.Name) && method.CaseSensitive.Value;
                if (!alwaysCaseSensitive && !HasCaseSensitiveCharacters(method.Members))
                {
                    alwaysCaseSensitive = true;
                }

                bool twoPart = method.Members.Max(x => x.ParseText.Length) > AsciiHash.MaxBytesHashed;
                if (alwaysCaseSensitive)
                {
                    if (twoPart)
                    {
                        NewLine().Append("global::RESPite.AsciiHash.HashCS(").Append(method.From.Name).Append(", out var cs0, out var cs1);");
                    }
                    else
                    {
                        NewLine().Append("var cs0 = global::RESPite.AsciiHash.HashCS(").Append(method.From.Name).Append(");");
                    }
                }
                else
                {
                    if (twoPart)
                    {
                        NewLine().Append("global::RESPite.AsciiHash.Hash(").Append(method.From.Name)
                            .Append(", out var cs0, out var uc0, out var cs1, out var uc1);");
                    }
                    else
                    {
                        NewLine().Append("global::RESPite.AsciiHash.Hash(").Append(method.From.Name)
                            .Append(", out var cs0, out var uc0);");
                    }
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
                    NewLine().Append("if (").Append(valueTarget).Append(" == (")
                        .Append(method.To.Type).Append(")").Append(method.DefaultValue).Append(")");
                    NewLine().Append("{");
                    indent++;
                    NewLine().Append("// by convention, init to zero on miss");
                    NewLine().Append(valueTarget).Append(" = default;");
                    NewLine().Append("return false;");
                    indent--;
                    NewLine().Append("}");
                    NewLine().Append("return true;");
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
                    NewLine().Append(valueTarget).Append(" = ").Append(method.From.Name).Append(".Length switch {");
                    indent++;
                    foreach (var member in method.Members
                                 .OrderBy(x => x.ParseText.Length)
                                 .ThenBy(x => x.ParseText))
                    {
                        var len = member.ParseText.Length;
                        AsciiHash.Hash(member.ParseText, out var cs0, out var uc0, out var cs1, out var uc1);

                        bool valueCaseSensitive = caseSensitive || !HasCaseSensitiveCharacters(member.ParseText);

                        line = NewLine().Append(len).Append(" when ");
                        if (twoPart) line.Append("(");
                        if (valueCaseSensitive)
                        {
                            line.Append("cs0 is ").Append(cs0);
                        }
                        else
                        {
                            line.Append("uc0 is ").Append(uc0);
                        }

                        if (len > AsciiHash.MaxBytesHashed)
                        {
                            if (valueCaseSensitive)
                            {
                                line.Append(" & cs1 is ").Append(cs1);
                            }
                            else
                            {
                                line.Append(" & uc1 is ").Append(uc1);
                            }
                        }
                        if (twoPart) line.Append(")");
                        if (len > 2 * AsciiHash.MaxBytesHashed)
                        {
                            line.Append(" && ");
                            var csValue = SyntaxFactory
                                .LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    SyntaxFactory.Literal(member.ParseText.Substring(2 * AsciiHash.MaxBytesHashed)))
                                .ToFullString();

                            line.Append("global::RESPite.AsciiHash.")
                                .Append(valueCaseSensitive ? nameof(AsciiHash.SequenceEqualsCS) : nameof(AsciiHash.SequenceEqualsCI))
                                .Append("(").Append(method.From.Name).Append(".Slice(").Append(2 * AsciiHash.MaxBytesHashed).Append("), ").Append(csValue);
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

    private static bool HasCaseSensitiveCharacters(string value)
    {
        foreach (char c in value ?? "")
        {
            if (char.IsLetter(c)) return true;
        }

        return false;
    }

    private static bool HasCaseSensitiveCharacters(BasicArray<(string EnumMember, string ParseText)> members)
    {
        // do we have alphabet characters? case sensitivity doesn't apply if not
        foreach (var member in members)
        {
            if (HasCaseSensitiveCharacters(member.ParseText)) return true;
        }

        return false;
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

                AsciiHash.Hash(literal.Value, out var hashCS, out var hashUC);
                NewLine().Append("static partial class ").Append(literal.Name);
                NewLine().Append("{");
                indent++;
                NewLine().Append("public const int Length = ").Append(literal.Value.Length).Append(';');
                NewLine().Append("public const long HashCS = ").Append(hashCS).Append(';');
                NewLine().Append("public const long HashUC = ").Append(hashUC).Append(';');
                NewLine().Append("public static ReadOnlySpan<byte> U8 => ").Append(csValue).Append("u8;");
                NewLine().Append("public const string Text = ").Append(csValue).Append(';');
                if (literal.Value.Length <= AsciiHash.MaxBytesHashed)
                {
                    // the case-sensitive hash enforces all the values
                    NewLine().Append(
                        "public static bool IsCS(ReadOnlySpan<byte> value, long cs) => cs == HashCS & value.Length == Length;");
                    NewLine().Append(
                        "public static bool IsCI(ReadOnlySpan<byte> value, long uc) => uc == HashUC & value.Length == Length;");
                }
                else
                {
                    NewLine().Append(
                        "public static bool IsCS(ReadOnlySpan<byte> value, long cs) => cs == HashCS && value.SequenceEqual(U8);");
                    NewLine().Append(
                        "public static bool IsCI(ReadOnlySpan<byte> value, long uc) => uc == HashUC && global::RESPite.AsciiHash.SequenceEqualsCI(value, U8);");
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
