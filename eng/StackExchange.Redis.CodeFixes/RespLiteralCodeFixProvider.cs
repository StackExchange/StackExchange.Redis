using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace StackExchange.Redis.CodeFixes;

/// <summary>
/// Fixes SER309 - literal text in a RESP command - by replacing the token with a hole referencing a declared
/// <c>[Resp]</c> fragment: <c>$"{key} nx {val}"</c> becomes <c>$"{key} {RespLiterals.Nx} {val}"</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the point of rejecting inline tokens rather than splitting them at runtime: you type it the way the
/// command reads, take the fix, and the committed code is the strict form. Making literal runs work instead
/// would cost the compile-time argument count on every call site that used them.
/// </para>
/// <para>
/// Two fixes. When a matching declaration exists, use it. When none does, declare it in the type containing
/// the call site - not an obviously right home, but the only one that needs no guessing, and it is trivially
/// movable afterwards. Together they mean the strict form costs a keystroke rather than a lookup.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RespLiteralCodeFixProvider))]
[Shared]
public sealed class RespLiteralCodeFixProvider : CodeFixProvider
{
    private const string LiteralNotSentId = "SER309";
    private const string TokenProperty = "Token";
    private const string RespAttributeName = "StackExchange.Redis.Interpolated.RespAttribute";
    private const string FragmentTypeName = "StackExchange.Redis.Interpolated.RespFragment";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(LiteralNotSentId);

    /// <inheritdoc/>
    // No FixAllProvider: each literal resolves to a different member, and a run that resolves to none is left
    // alone - "fix all" would imply a uniform answer that does not exist.
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(TokenProperty, out var token) || string.IsNullOrEmpty(token)) continue;

            // a run of several tokens has no single answer; leave it for the human
            if (token!.IndexOf(' ') >= 0) continue;

            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                is not InterpolatedStringTextSyntax text) continue;

            var match = FindFragment(model.Compilation, token!, context.CancellationToken);
            if (match is not null)
            {
                // built from the containing type rather than ToMinimalDisplayString(property), which includes
                // the property's TYPE and would produce "RespFragment RespLiterals.Nx"
                var name = match.ContainingType.ToMinimalDisplayString(model, text.SpanStart) + "." + match.Name;
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Use '" + name + "'",
                        createChangedDocument: _ => Task.FromResult(Apply(context.Document, root, text, name)),
                        equivalenceKey: LiteralNotSentId + ":use"),
                    diagnostic);
                continue;
            }

            // nothing declared: offer to declare it here. The containing type is not an obviously right home,
            // but it is the only one that needs no guessing, and moving it later is trivial.
            var host = text.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (host is null) continue;

            var fragmentType = model.Compilation.GetTypeByMetadataName(FragmentTypeName);
            if (fragmentType is null) continue;

            var member = MemberNameFor(token!);
            var typeName = fragmentType.ToMinimalDisplayString(model, host.SpanStart);
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Declare '" + member + "' here and use it",
                    createChangedDocument: _ => Task.FromResult(
                        Declare(context.Document, root, text, host, member, token!, typeName)),
                    equivalenceKey: LiteralNotSentId + ":declare"),
                diagnostic);
        }
    }

    /// <summary>
    /// Replace the literal with (space) (hole) (space), keeping the separators it had - a single space either
    /// side, since more than one is itself the diagnostic.
    /// </summary>
    private static Document Apply(Document document, SyntaxNode root, InterpolatedStringTextSyntax text, string name)
        => document.WithSyntaxRoot(root.ReplaceNode(text, Replacements(text, name)));

    private static List<InterpolatedStringContentSyntax> Replacements(InterpolatedStringTextSyntax text, string name)
    {
        var raw = text.TextToken.ValueText;
        var replacements = new List<InterpolatedStringContentSyntax>();

        if (raw.Length != 0 && char.IsWhiteSpace(raw[0])) replacements.Add(Text(" "));

        replacements.Add(SyntaxFactory.Interpolation(SyntaxFactory.ParseExpression(name)));

        if (raw.Length > 1 && char.IsWhiteSpace(raw[raw.Length - 1])) replacements.Add(Text(" "));

        return replacements;
    }

    /// <summary>
    /// Declare the fragment in <paramref name="host"/> and point the literal at it.
    /// </summary>
    /// <remarks>
    /// Both edits land in one tree, so the nodes are tracked across the first rewrite rather than re-found by
    /// span - which would be wrong the moment the first edit changes any offset.
    /// </remarks>
    private static Document Declare(
        Document document,
        SyntaxNode root,
        InterpolatedStringTextSyntax text,
        TypeDeclarationSyntax host,
        string member,
        string token,
        string fragmentTypeName)
    {
        var tracked = root.TrackNodes(text, host);

        var currentText = tracked.GetCurrentNode(text)!;
        var afterLiteral = tracked.ReplaceNode(currentText, Replacements(currentText, member));

        var currentHost = afterLiteral.GetCurrentNode(host)!;

        // the attribute only needs the token when inference would not produce it; inference upper-cases, so
        // "nx" needs nothing and "lib-name" does
        var attribute = string.Equals(member.ToUpperInvariant(), token.ToUpperInvariant(), StringComparison.Ordinal)
            ? "[Resp]"
            : "[Resp(\"" + token + "\")]";

        // attribute on its own line, with a blank line above, matching how these are normally written
        var declaration = SyntaxFactory.ParseMemberDeclaration(
            attribute + SyntaxFactory.ElasticCarriageReturnLineFeed
            + "private static partial " + fragmentTypeName + " " + member + " { get; }")!
            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newHost = currentHost.AddMembers(declaration);

        // the generator supplies the body as another part, so the type has to be partial
        if (!newHost.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            newHost = newHost.AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
        }

        return document.WithSyntaxRoot(afterLiteral.ReplaceNode(currentHost, newHost));
    }

    /// <summary>
    /// A token rendered as a member name: <c>lib-name</c> becomes <c>LibName</c>.
    /// </summary>
    /// <remarks>
    /// Word boundaries can only come from separators, so a single run stays a single word - <c>withsave</c>
    /// becomes <c>Withsave</c>, not <c>WithSave</c>. Guessing where words divide would need a dictionary, and
    /// would be wrong often enough to be worse than this; rename it afterwards if it matters.
    /// </remarks>
    private static string MemberNameFor(string token)
    {
        var sb = new System.Text.StringBuilder(token.Length);
        var upper = true;
        foreach (var c in token)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upper = true;
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            upper = false;
        }

        return sb.Length == 0 ? "Token" : sb.ToString();
    }

    private static InterpolatedStringTextSyntax Text(string value)
        => SyntaxFactory.InterpolatedStringText(
            SyntaxFactory.Token(default, SyntaxKind.InterpolatedStringTextToken, value, value, default));

    /// <summary>
    /// Find a <c>[Resp]</c> property in <em>source</em> whose token matches, case-insensitively.
    /// </summary>
    /// <remarks>
    /// Source only - walking every referenced assembly would be far more work for no benefit, since the
    /// declarations that matter are the ones the caller can see and edit.
    /// </remarks>
    private static IPropertySymbol? FindFragment(Compilation compilation, string token, CancellationToken cancellationToken)
    {
        var attribute = compilation.GetTypeByMetadataName(RespAttributeName);
        if (attribute is null) return null;

        foreach (var type in AllTypes(compilation.Assembly.GlobalNamespace, cancellationToken))
        {
            foreach (var member in type.GetMembers())
            {
                if (member is not IPropertySymbol property) continue;

                foreach (var data in property.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute)) continue;

                    var declared = TokenOf(data, property);
                    if (declared is not null && string.Equals(declared, token, StringComparison.OrdinalIgnoreCase))
                    {
                        return property;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The single token a declaration emits: the attribute's, or the member name upper-cased. Null when the
    /// fragment spans several tokens, which cannot match one inline token.
    /// </summary>
    private static string? TokenOf(AttributeData data, IPropertySymbol property)
    {
        var tokens = new List<string>();
        foreach (var arg in data.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Array)
            {
                foreach (var element in arg.Values)
                {
                    if (element.Value is string s) tokens.Add(s);
                }
            }
            else if (arg.Value is string s)
            {
                tokens.Add(s);
            }
        }

        return tokens.Count switch
        {
            0 => property.Name.ToUpperInvariant(),
            1 => tokens[0],
            _ => null,
        };
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns, CancellationToken cancellationToken)
    {
        foreach (var member in ns.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (member)
            {
                case INamespaceSymbol nested:
                    foreach (var type in AllTypes(nested, cancellationToken)) yield return type;
                    break;
                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in AllNested(type, cancellationToken)) yield return nested;
                    break;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> AllNested(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return nested;
            foreach (var deeper in AllNested(nested, cancellationToken)) yield return deeper;
        }
    }
}
