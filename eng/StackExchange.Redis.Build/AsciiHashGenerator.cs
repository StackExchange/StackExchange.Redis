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
    /// <summary>
    /// The emitted code uses UTF-8 string literals, which are C# 11.
    /// </summary>
    private const LanguageVersion MinimumLanguageVersion = LanguageVersions.CSharp11;

    /// <summary>
    /// The attribute that drives this generator, by metadata name.
    /// </summary>
    /// <remarks>
    /// Fully qualified, and matched by the host rather than by us: <c>ForAttributeWithMetadataName</c> indexes
    /// attributes across the compilation once and only calls us for real matches. The predicates below used to
    /// compare attribute *text* on every attribute in every file, which was both slower and looser - it would
    /// have matched an unrelated attribute that happened to be called <c>AsciiHash</c>, and missed one reached
    /// through an alias.
    /// </remarks>
    private const string AsciiHashAttributeName = "RESPite.AsciiHashAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // looking for [AsciiHash] partial static class Foo { }
        var types = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AsciiHashAttributeName,
                static (node, _) => node is ClassDeclarationSyntax decl && IsStaticPartial(decl.Modifiers),
                TransformTypes)
            .Where(pair => pair.Name is { Length: > 0 })
            .Collect();

        // looking for [AsciiHash] partial static bool TryParse(input, out output) { }
        var methods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AsciiHashAttributeName,
                static (node, _) => node is MethodDeclarationSyntax decl && IsStaticPartial(decl.Modifiers),
                TransformMethods)
            .Where(pair => pair.Name is { Length: > 0 })
            .Collect();

        // looking for [AsciiHash] partial static bool TryFormat(enum input, out string/ReadOnlySpan<byte> output) { }
        var formatMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AsciiHashAttributeName,
                static (node, _) => node is MethodDeclarationSyntax decl && IsStaticPartial(decl.Modifiers),
                TransformFormatMethods)
            .Where(pair => pair.Name is { Length: > 0 })
            .Collect();

        // looking for [AsciiHash("some type")] enum Foo { }
        var enums = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AsciiHashAttributeName,
                static (node, _) => node is EnumDeclarationSyntax,
                TransformEnums)
            .Where(pair => pair.Name is { Length: > 0 })
            .Collect();

        // The code we emit uses UTF-8 string literals ("..."u8), so it will not compile below C# 11. Old TFMs
        // default below that (netstandard2.0 and net472 default to C# 7.3), but the language version is not
        // tied to the target framework - any consumer on a .NET 7 or later SDK can opt in with <LangVersion>,
        // so this should be rare and is trivially fixable. The point is to *say* that: emitting anyway would
        // put errors inside generated code the consumer cannot edit, and emitting nothing silently would
        // surface as an unexplained "no implementing declaration". See Diagnostics.LanguageVersionTooLow.
        var languageVersion = context.ParseOptionsProvider.Select(static (options, _)
            => options is CSharpParseOptions cs ? cs.LanguageVersion.MapSpecifiedToEffectiveVersion() : LanguageVersion.Latest);

        context.RegisterSourceOutput(
            types.Combine(methods).Combine(formatMethods).Combine(enums).Combine(languageVersion),
            (ctx, content) =>
            {
                if (content.Right < MinimumLanguageVersion)
                {
                    // only complain if there was actually something to generate
                    var (t, m, f, e) = (content.Left.Left.Left.Left, content.Left.Left.Left.Right, content.Left.Left.Right, content.Left.Right);
                    if (t.Length + m.Length + f.Length + e.Length != 0)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.LanguageVersionTooLow,
                            location: null,
                            nameof(AsciiHashAttribute),
                            "11",
                            content.Right.ToDisplayString()));
                    }

                    return;
                }

                var left = content.Left;
                Generate(ctx, left.Left.Left.Left, left.Left.Left.Right, left.Left.Right, left.Right);
            });

        static bool IsStaticPartial(SyntaxTokenList tokens)
            => tokens.Any(SyntaxKind.StaticKeyword) && tokens.Any(SyntaxKind.PartialKeyword);
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
        GeneratorAttributeSyntaxContext ctx, CancellationToken cancellationToken)
    {
        // extract the name and value (defaults to name, but can be overridden via attribute) and the location
        if (ctx.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Enum } named) return default;
        // list patterns would need System.Index, which netstandard2.0 does not have
        if (ctx.Attributes.IsDefaultOrEmpty) return default;
        var attrib = ctx.Attributes[0];
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
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken cancellationToken)
    {
        // extract the name and value (defaults to name, but can be overridden via attribute) and the location
        if (ctx.TargetSymbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } named) return default;
        // list patterns would need System.Index, which netstandard2.0 does not have
        if (ctx.Attributes.IsDefaultOrEmpty) return default;
        var attrib = ctx.Attributes[0];

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
            // an explicit empty token means "pretend you don't know about this" (usually the zero
            // member, i.e. Unknown/None); anything else: infer from the name
            value = HasExplicitToken(asciiHashAttribute) ? "" : InferPayload(name);
        }

        return value;
    }

    // the attribute's token parameter is optional, so a bare [AsciiHash] and [AsciiHash("")] are
    // indistinguishable in ConstructorArguments (both report ""); the syntax tells them apart
    private static bool HasExplicitToken(AttributeData? asciiHashAttribute)
    {
        if (asciiHashAttribute?.ApplicationSyntaxReference?.GetSyntax()
            is not AttributeSyntax { ArgumentList.Arguments: { Count: > 0 } arguments })
        {
            return false;
        }

        foreach (var argument in arguments)
        {
            // NameEquals is a property assignment (i.e. CaseSensitive = false); anything else is
            // the token, whether positional or named (i.e. token: "")
            if (argument.NameEquals is null) return true;
        }

        return false;
    }

    private static string InferPayload(string name) => name.Replace("_", "-");

    private (string Namespace, string ParentType, Accessibility Accessibility, string Name,
        (string Type, string Name, bool IsBytes, RefKind RefKind) From, (string Type, string Name, RefKind RefKind) To,
        (string Name, bool Value, RefKind RefKind) CaseSensitive,
        BasicArray<(string EnumMember, string ParseText)> Members, int DefaultValue) TransformMethods(
            GeneratorAttributeSyntaxContext ctx,
            CancellationToken cancellationToken)
    {
        if (ctx.TargetSymbol is not IMethodSymbol
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

        // list patterns would need System.Index, which netstandard2.0 does not have
        if (ctx.Attributes.IsDefaultOrEmpty) return default;
        var attrib = ctx.Attributes[0];

        if (method.ContainingType is not { } containingType) return default;
        var parentType = GetName(containingType);
        var ns = containingType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        var arg = method.Parameters[0];
        if (arg is not { IsOptional: false, RefKind: RefKind.None or RefKind.In or RefKind.Ref or RefKinds.RefReadOnlyParameter }) return default;

        static bool IsBytes(ITypeSymbol type)
        {
            // byte[]
            if (type is IArrayTypeSymbol { ElementType: { SpecialType: SpecialType.System_Byte } })
                return true;

            // Span<byte> or ReadOnlySpan<byte>
            if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct, Arity: 1, Name: "Span" or "ReadOnlySpan",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
                    TypeArguments: { Length: 1 } ta }
                && ta[0].SpecialType == SpecialType.System_Byte)
            {
                return true;
            }
            return false;
        }

        var fromType = arg.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        bool fromBytes = IsBytes(arg.Type);
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
                    RefKind: RefKind.None or RefKind.In or RefKind.Ref or RefKinds.RefReadOnlyParameter,
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

    private (string Namespace, string ParentType, Accessibility Accessibility, string Name,
        (string Type, string Name, RefKind RefKind) From, (string Type, string Name, RefKind RefKind, bool IsBytes) To,
        BasicArray<(string EnumMember, string FormatText)> Members) TransformFormatMethods(
            GeneratorAttributeSyntaxContext ctx,
            CancellationToken cancellationToken)
    {
        if (ctx.TargetSymbol is not IMethodSymbol
            {
                IsStatic: true,
                IsPartialDefinition: true,
                PartialImplementationPart: null,
                Arity: 0,
                ReturnType.SpecialType: SpecialType.System_Boolean,
                Parameters:
                {
                    IsDefaultOrEmpty: false,
                    Length: 2,
                },
            } method) return default;

        if (ctx.Attributes.IsDefaultOrEmpty) return default;

        if (method.ContainingType is not { } containingType) return default;
        var parentType = GetName(containingType);
        var ns = containingType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        var arg = method.Parameters[0];
        if (arg is not
            {
                IsOptional: false,
                RefKind: RefKind.None or RefKind.In or RefKind.Ref or RefKinds.RefReadOnlyParameter,
                Type: INamedTypeSymbol { TypeKind: TypeKind.Enum },
            }) return default;
        var from = (arg.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), arg.Name, arg.RefKind);

        var enumMembers = arg.Type.GetMembers();
        var builder = new BasicArray<(string EnumMember, string FormatText)>.Builder(enumMembers.Length);
        HashSet<object> values = new();
        foreach (var member in enumMembers)
        {
            if (member is IFieldSymbol { IsStatic: true, IsConst: true } field)
            {
                var rawValue = GetRawValue(field.Name, TryGetAsciiHashAttribute(member.GetAttributes()));
                if (string.IsNullOrWhiteSpace(rawValue)) continue;
                if (field.ConstantValue is { } constValue && !values.Add(constValue)) continue;
                builder.Add((field.Name, rawValue));
            }
        }

        arg = method.Parameters[1];
        if (arg is not
            {
                IsOptional: false,
                RefKind: RefKind.Out,
            }) return default;
        bool toBytes = IsReadOnlySpanOfByte(arg.Type);
        if (arg.Type.SpecialType != SpecialType.System_String && !toBytes) return default;
        var to = (arg.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), arg.Name, arg.RefKind, toBytes);

        return (ns, parentType, method.DeclaredAccessibility, method.Name, from, to, builder.Build());

        static bool IsReadOnlySpanOfByte(ITypeSymbol type)
        {
            return type is INamedTypeSymbol
            {
                TypeKind: TypeKind.Struct,
                Arity: 1,
                Name: "ReadOnlySpan",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
                TypeArguments: { Length: 1 } typeArguments,
            } && typeArguments[0].SpecialType == SpecialType.System_Byte;
        }
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
        ImmutableArray<(string Namespace, string ParentType, Accessibility Accessibility, string Name,
            (string Type, string Name, RefKind RefKind) From,
            (string Type, string Name, RefKind RefKind, bool IsBytes) To,
            BasicArray<(string EnumMember, string FormatText)> Members)> formatMethods,
        ImmutableArray<(string Namespace, string ParentType, string Name, int Count, int MaxChars, int MaxBytes)> enums)
    {
        if (types.IsDefaultOrEmpty & parseMethods.IsDefaultOrEmpty & formatMethods.IsDefaultOrEmpty & enums.IsDefaultOrEmpty) return; // nothing to do

        var sb = new StringBuilder("// <auto-generated />")
            .AppendLine().Append("// ").Append(GetType().Name).Append(" v").Append(GetVersion()).AppendLine();

        sb.AppendLine("using System;");
        sb.AppendLine("using StackExchange.Redis;");
        sb.AppendLine("#pragma warning disable CS8981, SER004");

        var writer = new CodeWriter(sb);
        BuildTypeImplementations(writer, types);
        BuildEnumParsers(writer, parseMethods);
        BuildEnumFormatters(writer, formatMethods);
        BuildEnumLengths(writer, enums);
        ctx.AddSource(nameof(AsciiHash) + ".generated.cs", sb.ToString());
    }

    private void BuildEnumLengths(CodeWriter writer, ImmutableArray<(string Namespace, string ParentType, string Name, int Count, int MaxChars, int MaxBytes)> enums)
    {
        if (enums.IsDefaultOrEmpty) return; // nope

        foreach (var grp in enums.GroupBy(l => (l.Namespace, l.ParentType)))
        {
            writer.NewLine();
            int braces = 0;
            if (!string.IsNullOrWhiteSpace(grp.Key.Namespace))
            {
                writer.NewLine().Append("namespace ").Append(grp.Key.Namespace);
                writer.NewLine().Append("{");
                writer.Indent();
                braces++;
            }

            if (!string.IsNullOrWhiteSpace(grp.Key.ParentType))
            {
                if (grp.Key.ParentType.Contains('.')) // nested types
                {
                    foreach (var part in grp.Key.ParentType.Split('.'))
                    {
                        writer.NewLine().Append("partial class ").Append(part);
                        writer.NewLine().Append("{");
                        writer.Indent();
                        braces++;
                    }
                }
                else
                {
                    writer.NewLine().Append("partial class ").Append(grp.Key.ParentType);
                    writer.NewLine().Append("{");
                    writer.Indent();
                    braces++;
                }
            }

            foreach (var @enum in grp)
            {
                writer.NewLine().Append("internal static partial class ").Append(@enum.Name);
                writer.NewLine().Append("{");
                writer.Indent();
                writer.NewLine().Append("public const int EnumCount = ").Append(@enum.Count).Append(";");
                writer.NewLine().Append("public const int MaxChars = ").Append(@enum.MaxChars).Append(";");
                writer.NewLine().Append("public const int MaxBytes = ").Append(@enum.MaxBytes).Append("; // as UTF8");
                // for buffer bytes: we want to allow 1 extra byte (to check for false-positive over-long values),
                // and then round up to the nearest multiple of 8 (for stackalloc performance, etc)
                int bufferBytes = (@enum.MaxBytes + 1 + 7) & ~7;
                writer.NewLine().Append("public const int BufferBytes = ").Append(bufferBytes).Append(";");
                writer.Outdent();
                writer.NewLine().Append("}");
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                writer.Outdent();
                writer.NewLine().Append("}");
            }
        }
    }

    private void BuildEnumParsers(
        CodeWriter writer,
        in ImmutableArray<(string Namespace, string ParentType, Accessibility Accessibility, string Name,
            (string Type, string Name, bool IsBytes, RefKind RefKind) From,
            (string Type, string Name, RefKind RefKind) To,
            (string Name, bool Value, RefKind RefKind) CaseSensitive,
            BasicArray<(string EnumMember, string ParseText)> Members, int DefaultValue)> enums)
    {
        if (enums.IsDefaultOrEmpty) return; // nope

        foreach (var grp in enums.GroupBy(l => (l.Namespace, l.ParentType)))
        {
            writer.NewLine();
            int braces = 0;
            if (!string.IsNullOrWhiteSpace(grp.Key.Namespace))
            {
                writer.NewLine().Append("namespace ").Append(grp.Key.Namespace);
                writer.NewLine().Append("{");
                writer.Indent();
                braces++;
            }

            if (!string.IsNullOrWhiteSpace(grp.Key.ParentType))
            {
                if (grp.Key.ParentType.Contains('.')) // nested types
                {
                    foreach (var part in grp.Key.ParentType.Split('.'))
                    {
                        writer.NewLine().Append("partial class ").Append(part);
                        writer.NewLine().Append("{");
                        writer.Indent();
                        braces++;
                    }
                }
                else
                {
                    writer.NewLine().Append("partial class ").Append(grp.Key.ParentType);
                    writer.NewLine().Append("{");
                    writer.Indent();
                    braces++;
                }
            }

            foreach (var method in grp)
            {
                var line = writer.NewLine().Append(Format(method.Accessibility)).Append(" static partial bool ")
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
                writer.NewLine().Append("{");
                writer.Indent();
                writer.NewLine().Append("// ").Append(method.To.Type).Append(" has ").Append(method.Members.Length).Append(" members");
                string valueTarget = method.To.Name;
                if (method.To.RefKind != RefKind.Out)
                {
                    valueTarget = "__tmp";
                    writer.NewLine().Append(method.To.Type).Append(" ").Append(valueTarget).Append(";");
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
                        writer.NewLine().Append("global::RESPite.AsciiHash.HashCS(").Append(method.From.Name).Append(", out var cs0, out var cs1);");
                    }
                    else
                    {
                        writer.NewLine().Append("var cs0 = global::RESPite.AsciiHash.HashCS(").Append(method.From.Name).Append(");");
                    }
                }
                else
                {
                    if (twoPart)
                    {
                        writer.NewLine().Append("global::RESPite.AsciiHash.Hash(").Append(method.From.Name)
                            .Append(", out var cs0, out var uc0, out var cs1, out var uc1);");
                    }
                    else
                    {
                        writer.NewLine().Append("global::RESPite.AsciiHash.Hash(").Append(method.From.Name)
                            .Append(", out var cs0, out var uc0);");
                    }
                }

                if (string.IsNullOrEmpty(method.CaseSensitive.Name))
                {
                    Write(method.CaseSensitive.Value);
                }
                else
                {
                    writer.NewLine().Append("if (").Append(method.CaseSensitive.Name).Append(")");
                    writer.NewLine().Append("{");
                    writer.Indent();
                    Write(true);
                    writer.Outdent();
                    writer.NewLine().Append("}");
                    writer.NewLine().Append("else");
                    writer.NewLine().Append("{");
                    writer.Indent();
                    Write(false);
                    writer.Outdent();
                    writer.NewLine().Append("}");
                }

                if (method.To.RefKind == RefKind.Out)
                {
                    writer.NewLine().Append("if (").Append(valueTarget).Append(" == (")
                        .Append(method.To.Type).Append(")").Append(method.DefaultValue).Append(")");
                    writer.NewLine().Append("{");
                    writer.Indent();
                    writer.NewLine().Append("// by convention, init to zero on miss");
                    writer.NewLine().Append(valueTarget).Append(" = default;");
                    writer.NewLine().Append("return false;");
                    writer.Outdent();
                    writer.NewLine().Append("}");
                    writer.NewLine().Append("return true;");
                }
                else
                {
                    writer.NewLine().Append("// do not update parameter on miss");
                    writer.NewLine().Append("if (").Append(valueTarget).Append(" == (")
                        .Append(method.To.Type).Append(")").Append(method.DefaultValue).Append(") return false;");
                    writer.NewLine().Append(method.To.Name).Append(" = ").Append(valueTarget).Append(";");
                    writer.NewLine().Append("return true;");
                }

                void Write(bool caseSensitive)
                {
                    writer.NewLine().Append(valueTarget).Append(" = ").Append(method.From.Name).Append(".Length switch {");
                    writer.Indent();
                    foreach (var member in method.Members
                                 .OrderBy(x => x.ParseText.Length)
                                 .ThenBy(x => x.ParseText))
                    {
                        var len = member.ParseText.Length;
                        AsciiHash.Hash(member.ParseText, out var cs0, out var uc0, out var cs1, out var uc1);

                        bool valueCaseSensitive = caseSensitive || !HasCaseSensitiveCharacters(member.ParseText);

                        line = writer.NewLine().Append(len).Append(" when ");
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

                    writer.NewLine().Append("_ => (").Append(method.To.Type).Append(")").Append(method.DefaultValue)
                        .Append(",");
                    writer.Outdent();
                    writer.NewLine().Append("};");
                }

                writer.Outdent();
                writer.NewLine().Append("}");
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                writer.Outdent();
                writer.NewLine().Append("}");
            }
        }
    }

    private void BuildEnumFormatters(
        CodeWriter writer,
        in ImmutableArray<(string Namespace, string ParentType, Accessibility Accessibility, string Name,
            (string Type, string Name, RefKind RefKind) From,
            (string Type, string Name, RefKind RefKind, bool IsBytes) To,
            BasicArray<(string EnumMember, string FormatText)> Members)> enums)
    {
        if (enums.IsDefaultOrEmpty) return; // nope

        foreach (var grp in enums.GroupBy(l => (l.Namespace, l.ParentType)))
        {
            writer.NewLine();
            int braces = 0;
            if (!string.IsNullOrWhiteSpace(grp.Key.Namespace))
            {
                writer.NewLine().Append("namespace ").Append(grp.Key.Namespace);
                writer.NewLine().Append("{");
                writer.Indent();
                braces++;
            }

            if (!string.IsNullOrWhiteSpace(grp.Key.ParentType))
            {
                if (grp.Key.ParentType.Contains('.')) // nested types
                {
                    foreach (var part in grp.Key.ParentType.Split('.'))
                    {
                        writer.NewLine().Append("partial class ").Append(part);
                        writer.NewLine().Append("{");
                        writer.Indent();
                        braces++;
                    }
                }
                else
                {
                    writer.NewLine().Append("partial class ").Append(grp.Key.ParentType);
                    writer.NewLine().Append("{");
                    writer.Indent();
                    braces++;
                }
            }

            foreach (var method in grp)
            {
                writer.NewLine().Append(Format(method.Accessibility)).Append(" static partial bool ")
                    .Append(method.Name).Append("(")
                    .Append(Format(method.From.RefKind))
                    .Append(method.From.Type).Append(" ").Append(method.From.Name).Append(", ")
                    .Append(Format(method.To.RefKind))
                    .Append(method.To.Type).Append(" ").Append(method.To.Name)
                    .Append(")");

                writer.NewLine().Append("{");
                writer.Indent();
                writer.NewLine().Append("// ").Append(method.From.Type).Append(" has ").Append(method.Members.Length).Append(" formatted members");
                writer.NewLine().Append("switch (").Append(method.From.Name).Append(")");
                writer.NewLine().Append("{");
                writer.Indent();

                foreach (var member in method.Members)
                {
                    var formatted = SyntaxFactory
                        .LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(member.FormatText))
                        .ToFullString();
                    if (method.To.IsBytes) formatted += "u8";

                    writer.NewLine().Append("case ").Append(method.From.Type).Append(".").Append(member.EnumMember).Append(":");
                    writer.Indent();
                    writer.NewLine().Append(method.To.Name).Append(" = ").Append(formatted).Append(";");
                    writer.NewLine().Append("return true;");
                    writer.Outdent();
                }

                writer.NewLine().Append("default:");
                writer.Indent();
                writer.NewLine().Append(method.To.Name).Append(" = ").Append(method.To.IsBytes ? "default" : "default!").Append(";");
                writer.NewLine().Append("return false;");
                writer.Outdent();
                writer.Outdent();
                writer.NewLine().Append("}");
                writer.Outdent();
                writer.NewLine().Append("}");
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                writer.Outdent();
                writer.NewLine().Append("}");
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
        RefKinds.RefReadOnlyParameter or RefKind.RefReadOnly => "ref readonly ",
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
        CodeWriter writer,
        in ImmutableArray<(string Namespace, string ParentType, string Name, string Value)> types)
    {
        if (types.IsDefaultOrEmpty) return; // nope

        foreach (var grp in types.GroupBy(l => (l.Namespace, l.ParentType)))
        {
            writer.NewLine();
            int braces = 0;
            if (!string.IsNullOrWhiteSpace(grp.Key.Namespace))
            {
                writer.NewLine().Append("namespace ").Append(grp.Key.Namespace);
                writer.NewLine().Append("{");
                writer.Indent();
                braces++;
            }

            if (!string.IsNullOrWhiteSpace(grp.Key.ParentType))
            {
                if (grp.Key.ParentType.Contains('.')) // nested types
                {
                    foreach (var part in grp.Key.ParentType.Split('.'))
                    {
                        writer.NewLine().Append("partial class ").Append(part);
                        writer.NewLine().Append("{");
                        writer.Indent();
                        braces++;
                    }
                }
                else
                {
                    writer.NewLine().Append("partial class ").Append(grp.Key.ParentType);
                    writer.NewLine().Append("{");
                    writer.Indent();
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
                writer.NewLine().Append("static partial class ").Append(literal.Name);
                writer.NewLine().Append("{");
                writer.Indent();
                writer.NewLine().Append("public const int Length = ").Append(literal.Value.Length).Append(';');
                writer.NewLine().Append("public const long HashCS = ").Append(hashCS).Append(';');
                writer.NewLine().Append("public const long HashUC = ").Append(hashUC).Append(';');
                writer.NewLine().Append("public static ReadOnlySpan<byte> U8 => ").Append(csValue).Append("u8;");
                writer.NewLine().Append("public const string Text = ").Append(csValue).Append(';');
                if (literal.Value.Length <= AsciiHash.MaxBytesHashed)
                {
                    // the case-sensitive hash enforces all the values
                    writer.NewLine().Append(
                        "public static bool IsCS(ReadOnlySpan<byte> value, long cs) => cs == HashCS & value.Length == Length;");
                    writer.NewLine().Append(
                        "public static bool IsCI(ReadOnlySpan<byte> value, long uc) => uc == HashUC & value.Length == Length;");
                }
                else
                {
                    writer.NewLine().Append(
                        "public static bool IsCS(ReadOnlySpan<byte> value, long cs) => cs == HashCS && value.SequenceEqual(U8);");
                    writer.NewLine().Append(
                        "public static bool IsCI(ReadOnlySpan<byte> value, long uc) => uc == HashUC && global::RESPite.AsciiHash.SequenceEqualsCI(value, U8);");
                }

                writer.Outdent();
                writer.NewLine().Append("}");
            }

            // handle any closing braces
            while (braces-- > 0)
            {
                writer.Outdent();
                writer.NewLine().Append("}");
            }
        }
    }
}
