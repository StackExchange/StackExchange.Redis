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
/// Only offered when a matching declaration already exists in source. Declaring one on the caller's behalf
/// would mean choosing a type to put it in, which is a judgement this cannot make.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RespLiteralCodeFixProvider))]
[Shared]
public sealed class RespLiteralCodeFixProvider : CodeFixProvider
{
    private const string LiteralNotSentId = "SER309";
    private const string TokenProperty = "Token";
    private const string RespAttributeName = "StackExchange.Redis.Interpolated.RespAttribute";

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
            if (match is null) continue;

            // built from the containing type rather than ToMinimalDisplayString(property), which includes
            // the property's TYPE and would produce "RespFragment RespLiterals.Nx"
            var name = match.ContainingType.ToMinimalDisplayString(model, text.SpanStart) + "." + match.Name;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use '" + name + "'",
                    createChangedDocument: _ => Task.FromResult(Apply(context.Document, root, text, name)),
                    equivalenceKey: LiteralNotSentId),
                diagnostic);
        }
    }

    /// <summary>
    /// Replace the literal with (space) (hole) (space), keeping the separators it had - a single space either
    /// side, since more than one is itself the diagnostic.
    /// </summary>
    private static Document Apply(Document document, SyntaxNode root, InterpolatedStringTextSyntax text, string name)
    {
        var raw = text.TextToken.ValueText;
        var replacements = new List<InterpolatedStringContentSyntax>();

        if (raw.Length != 0 && char.IsWhiteSpace(raw[0])) replacements.Add(Text(" "));

        replacements.Add(SyntaxFactory.Interpolation(SyntaxFactory.ParseExpression(name)));

        if (raw.Length > 1 && char.IsWhiteSpace(raw[raw.Length - 1])) replacements.Add(Text(" "));

        return document.WithSyntaxRoot(root.ReplaceNode(text, replacements));
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
