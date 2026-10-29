using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StackExchange.Redis.Build;

/// <summary>
/// Implements <c>[Resp] partial RespFragment Foo { get; }</c> by emitting the body: the tokens, pre-framed as
/// RESP bulk strings, with the argument count that goes with them - and <c>[Resp] partial RespCommand Foo { get; }</c>,
/// a command name resolved once.
/// </summary>
/// <remarks>
/// <para>
/// The point is that framing, length prefixes, casing and the argument count are correct <i>by construction</i>
/// rather than by review. Hand-written fragments are gated behind SER011 precisely because none of that is
/// checkable at the point of use, and malformed bytes desync the connection for every subsequent command.
/// </para>
/// <para>
/// Casing: a token taken from the member name is upper-cased, which is right for ~84% of the tokens this
/// library sends; a token given in the attribute is used verbatim, because the rest are values rather than
/// keywords (<c>yes</c>, <c>lib-name</c>, <c>replica</c>, the geo units) and lower-case is what goes on the
/// wire today.
/// </para>
/// <para>
/// <b>Commands go through <c>Command()</c>, not raw bytes.</b> A <c>RespCommand</c> has an identity a fragment
/// does not: a name the client knows resolves to it, so the connection's command map can still rename or disable
/// it. So the generated body is a static field initialised by <c>"NAME"u8.Command()</c> - the name validated here,
/// at build time, and the framing (for a name the client does not know) still done by the library, once.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public class RespFragmentGenerator : IIncrementalGenerator
{
    private const string RespAttributeName = "StackExchange.Redis.Protocol.RespAttribute";
    private const string FragmentType = "global::StackExchange.Redis.Protocol.RespFragment";
    private const string CommandType = "global::StackExchange.Redis.Protocol.RespCommand";
    private const string CommandFactory = "global::StackExchange.Redis.Protocol.RespCommands.Command";

    /// <summary>u8 literals need C# 11; below that we say so rather than emitting code that cannot compile.</summary>
    private const LanguageVersion MinimumLanguageVersion = LanguageVersions.CSharp11;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var fragments = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RespAttributeName,
                // deliberately NOT filtered to partial here: a [Resp] that cannot be implemented is
                // reported rather than skipped in silence, which would surface only as CS9248
                static (node, _) => node is PropertyDeclarationSyntax,
                Transform)
            .Where(static x => x is not null)
            .Collect();

        var languageVersion = context.ParseOptionsProvider.Select(static (options, _)
            => options is CSharpParseOptions cs ? cs.LanguageVersion.MapSpecifiedToEffectiveVersion() : LanguageVersion.Latest);

        context.RegisterSourceOutput(fragments.Combine(languageVersion), static (ctx, content) =>
        {
            var (found, version) = (content.Left, content.Right);
            if (found.IsDefaultOrEmpty) return;

            if (version < MinimumLanguageVersion)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.LanguageVersionTooLow, null, version.ToDisplayString(), MinimumLanguageVersion.ToDisplayString()));
                return;
            }

            var usable = ImmutableArray.CreateBuilder<FragmentInfo>();
            foreach (var fragment in found)
            {
                if (fragment is null) continue;
                if (fragment.Problem is { Length: > 0 })
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.RespFragmentNotGenerated, fragment.Location, fragment.Name, fragment.Problem));
                    continue;
                }

                usable.Add(fragment);
            }

            Emit(ctx, usable.ToImmutable());
        });
    }

    private static FragmentInfo? Transform(GeneratorAttributeSyntaxContext context, System.Threading.CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not IPropertySymbol property) return null;

        var location = context.TargetNode.GetLocation();
        var declaredType = property.Type.ToDisplayString();
        var isCommand = declaredType == "StackExchange.Redis.Protocol.RespCommand";
        if (!isCommand && declaredType != "StackExchange.Redis.Protocol.RespFragment")
        {
            return FragmentInfo.Rejected(property.Name, location, $"its type is '{property.Type.Name}', not RespFragment or RespCommand");
        }

        if (context.TargetNode is not PropertyDeclarationSyntax { } decl || !decl.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return FragmentInfo.Rejected(property.Name, location, "it is not declared 'partial', so there is no body to supply");
        }

        var tokens = ImmutableArray<string>.Empty;
        foreach (var attribute in context.Attributes)
        {
            var flat = new List<string>();
            foreach (var arg in attribute.ConstructorArguments)
            {
                if (arg.Kind == TypedConstantKind.Array)
                {
                    foreach (var element in arg.Values)
                    {
                        if (element.Value is string s) flat.Add(s);
                    }
                }
                else if (arg.Value is string s)
                {
                    flat.Add(s);
                }
            }

            tokens = flat.ToImmutableArray();
        }

        // no tokens given: infer one from the member name, upper-cased (see the remarks on this type)
        if (tokens.IsEmpty) tokens = ImmutableArray.Create(property.Name.ToUpperInvariant());

        if (isCommand)
        {
            // a command is one token; anything after it is an operand, and belongs in a fragment
            if (tokens.Length != 1)
            {
                return FragmentInfo.Rejected(property.Name, location, $"a RespCommand is a single command name, but {tokens.Length} tokens were given; put the subcommand or operands in a RespFragment");
            }

            // the checks Command() makes at run time, made here instead: a bad name fails the build, not the first call
            foreach (var c in tokens[0])
            {
                if (c <= ' ' || c > '~')
                {
                    return FragmentInfo.Rejected(property.Name, location, $"the command name '{tokens[0]}' must be printable ASCII without whitespace");
                }
            }
        }

        var containers = new List<string>();
        for (var type = property.ContainingType; type is not null; type = type.ContainingType)
        {
            containers.Insert(0, DeclarationOf(type));
        }

        return new FragmentInfo(
            property.ContainingNamespace.IsGlobalNamespace ? null : property.ContainingNamespace.ToDisplayString(),
            containers.ToImmutableArray(),
            property.Name,
            AccessibilityOf(property.DeclaredAccessibility),
            property.IsStatic,
            tokens,
            location,
            null,
            isCommand);
    }

    private static string DeclarationOf(INamedTypeSymbol type)
    {
        var kind = type.TypeKind switch
        {
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            _ => type.IsRecord ? "record" : "class",
        };
        return $"partial {kind} {type.Name}";
    }

    private static string AccessibilityOf(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "private",
    };

    private static void Emit(SourceProductionContext ctx, ImmutableArray<FragmentInfo> fragments)
    {
        var sb = new StringBuilder("// <auto-generated />").AppendLine()
            .Append("// ").Append(nameof(RespFragmentGenerator)).AppendLine()
            .AppendLine("#nullable enable")
            // SER011: hand-constructing a fragment. We ARE the sanctioned
            // construction site, so the suppression belongs here and nowhere wider.
            .AppendLine("#pragma warning disable SER011");

        var writer = new CodeWriter(sb);
        if (fragments.IsDefaultOrEmpty) return;

        foreach (var group in fragments.GroupBy(f => (f.Namespace, Containers: string.Join("+", f.Containers))))
        {
            var first = group.First();
            var depth = 0;

            if (first.Namespace is { Length: > 0 })
            {
                writer.Append("namespace ").Append(first.Namespace).NewLine().Append("{").NewLine().Indent();
                depth++;
            }

            foreach (var container in first.Containers)
            {
                writer.Append(container).NewLine().Append("{").NewLine().Indent();
                depth++;
            }

            foreach (var fragment in group)
            {
                WriteFragment(writer, fragment);
            }

            while (depth-- > 0)
            {
                writer.Outdent().Append("}").NewLine();
            }
        }

        ctx.AddSource("RespFragments.generated.cs", sb.ToString());
    }

    private static void WriteFragment(CodeWriter writer, FragmentInfo fragment)
    {
        if (fragment.IsCommand)
        {
            WriteCommand(writer, fragment);
            return;
        }

        var bytes = new StringBuilder();
        foreach (var token in fragment.Tokens)
        {
            bytes.Append('$').Append(Encoding.UTF8.GetByteCount(token)).Append("\\r\\n").Append(Escape(token)).Append("\\r\\n");
        }

        writer.Append("/// <summary>");
        for (int i = 0; i < fragment.Tokens.Length; i++)
        {
            if (i != 0) writer.Append(' ');
            writer.Append("<c>").Append(EscapeXml(fragment.Tokens[i])).Append("</c>");
        }

        writer.Append(fragment.Tokens.Length == 1 ? ", as a RESP bulk string." : ", as RESP bulk strings.").Append("</summary>").NewLine();

        writer.Append(fragment.Accessibility).Append(' ');
        if (fragment.IsStatic) writer.Append("static ");
        writer.Append("partial ").Append(FragmentType).Append(' ').Append(fragment.Name)
              .Append(" => new(\"").Append(bytes.ToString()).Append("\"u8");
        if (fragment.Tokens.Length != 1) writer.Append(", ").Append(fragment.Tokens.Length);
        writer.Append(");").NewLine().NewLine();
    }

    /// <summary>A command: a static field resolved once, and the property returning it.</summary>
    /// <remarks>
    /// The field is static even for an instance property: the name is a constant, so one resolution serves every
    /// instance. Its name is mangled so it cannot collide with anything the author declared.
    /// </remarks>
    private static void WriteCommand(CodeWriter writer, FragmentInfo fragment)
    {
        var token = fragment.Tokens[0];
        var field = "__RespCommand_" + fragment.Name;

        writer.Append("private static readonly ").Append(CommandType).Append(' ').Append(field)
              .Append(" = ").Append(CommandFactory).Append("(\"").Append(Escape(token)).Append("\"u8);").NewLine();

        writer.Append("/// <summary>The <c>").Append(EscapeXml(token)).Append("</c> command, resolved once.</summary>").NewLine();
        writer.Append(fragment.Accessibility).Append(' ');
        if (fragment.IsStatic) writer.Append("static ");
        writer.Append("partial ").Append(CommandType).Append(' ').Append(fragment.Name)
              .Append(" => ").Append(field).Append(';').NewLine().NewLine();
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    private static string EscapeXml(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>What one declaration needs for its body to be emitted.</summary>
    /// <remarks>Not a record: this project targets netstandard2.0, which has no <c>IsExternalInit</c>.</remarks>
    private sealed class FragmentInfo(
        string? ns,
        ImmutableArray<string> containers,
        string name,
        string accessibility,
        bool isStatic,
        ImmutableArray<string> tokens,
        Location? location,
        string? problem,
        bool isCommand = false)
    {
        /// <summary>A declaration that cannot be implemented, carrying why.</summary>
        public static FragmentInfo Rejected(string name, Location location, string problem)
            => new(null, ImmutableArray<string>.Empty, name, "private", false, ImmutableArray<string>.Empty, location, problem);

        public Location? Location => location;

        public string? Problem => problem;

        public string? Namespace => ns;

        public ImmutableArray<string> Containers => containers;

        public string Name => name;

        public string Accessibility => accessibility;

        public bool IsStatic => isStatic;

        public ImmutableArray<string> Tokens => tokens;

        public bool IsCommand => isCommand;
    }
}
