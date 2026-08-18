using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace StackExchange.Redis.CodeFixes;

/// <summary>
/// Fixes SER305/SER306 - waiting for the result of a command queued on a transaction or batch - by either
/// discarding the result, or capturing it and awaiting it after <c>Execute[Async]</c>.
/// </summary>
/// <remarks>
/// <para>
/// The IDs are literals rather than a reference to <c>Diagnostics</c>: that type is internal to the analyzer
/// assembly, which this deliberately does not reference (see the csproj). They are a published contract in any
/// case, so they cannot drift without a deliberate decision.
/// </para>
/// <para>
/// Both fixes work from the diagnostic's <em>additional</em> location, which the analyzer sets to the queued
/// call inside the wait. That avoids re-deriving "which invocation is being waited for" here, where getting it
/// wrong on a nested call - <c>await tran.StringGetAsync(GetKey())</c> - would rewrite the wrong expression.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(QueuedResultCodeFixProvider))]
[Shared]
public sealed class QueuedResultCodeFixProvider : CodeFixProvider
{
    private const string AwaitBeforeExecuteId = "SER305", FireAndForgetId = "SER306";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(AwaitBeforeExecuteId, FireAndForgetId);

    /// <inheritdoc/>
    // No FixAllProvider: the two fixes are alternatives rather than one obvious answer, and "capture it and
    // await it after Execute" moves statements around, which is not something to apply unread across a
    // solution. Offering BatchFixer here would make "fix all occurrences" appear and quietly pick discard.
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.AdditionalLocations.Count == 0) continue;
            if (root.FindNode(diagnostic.AdditionalLocations[0].SourceSpan, getInnermostNodeForTie: true)
                is not InvocationExpressionSyntax queued) continue;

            var wait = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (wait is null) continue;

            // Every rewrite below replaces or moves a whole statement, so a wait buried in a larger expression
            // - an argument, a ternary arm - is left alone: there is no edit that keeps the surrounding
            // expression meaningful, and a partial one would be worse than the squiggle.
            if (StatementFor(wait) is not { } statement) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Discard the queued result",
                    token => DiscardAsync(context.Document, root, statement, queued),
                    equivalenceKey: nameof(DiscardAsync)),
                diagnostic);

            // Capturing and awaiting later is only meaningful for SER305: a fire-and-forget result is the
            // default value whenever it is read, so moving the read later buys nothing at all (see SER306).
            // Restricted to the `await` form, since the sync shapes are in a method with no await to move to.
            if (diagnostic.Id != AwaitBeforeExecuteId || wait is not AwaitExpressionSyntax) continue;

            var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (model is null) continue;
            if (!CanCaptureAndAwaitLater(model, statement, queued, context.CancellationToken, out var execute)) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Capture it and await after Execute",
                    token => CaptureAsync(context.Document, model, statement, queued, execute, token),
                    equivalenceKey: nameof(CaptureAsync)),
                diagnostic);
        }
    }

    /// <summary>The statement the wait forms on its own, if it forms one.</summary>
    private static StatementSyntax? StatementFor(SyntaxNode wait) => wait.Parent switch
    {
        // await tran.StringSetAsync(key, value);
        ExpressionStatementSyntax statement => statement,

        // var value = await tran.StringGetAsync(key);
        EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax local } declaration } }
            when declaration.Variables.Count == 1 => local,

        _ => null,
    };

    private static Task<Document> DiscardAsync(Document document, SyntaxNode root, StatementSyntax statement, InvocationExpressionSyntax queued)
    {
        // "_ = tran.StringGetAsync(key);" rather than a bare expression statement, which would be CS0201 for
        // anything but an invocation - and reads as deliberate, which is the point of the fix
        var discard = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("_"),
                queued.WithoutTrivia()))
            .WithTriviaFrom(statement)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(statement, discard)));
    }

    /// <summary>
    /// Whether the capture-and-await-later rewrite is safe here, and if so which statement executes the batch.
    /// </summary>
    /// <remarks>
    /// Two things have to hold, and the second is the subtle one: the <c>Execute[Async]</c> must be a later
    /// statement in the same block, and - where the wait declares a variable - nothing between the two may
    /// already refer to that variable, since the declaration is what moves. Rewriting regardless would produce
    /// CS0841 ("cannot use before it is declared"), i.e. a fix that breaks the build it was offered to repair.
    /// </remarks>
    private static bool CanCaptureAndAwaitLater(
        SemanticModel model,
        StatementSyntax statement,
        InvocationExpressionSyntax queued,
        CancellationToken cancellationToken,
        out StatementSyntax execute)
    {
        execute = null!;
        if (statement.Parent is not BlockSyntax block) return false;
        if (queued.Expression is not MemberAccessExpressionSyntax { Expression: { } receiver }) return false;
        if (model.GetSymbolInfo(receiver, cancellationToken).Symbol is not { } receiverSymbol) return false;

        var index = block.Statements.IndexOf(statement);
        if (index < 0) return false;

        for (int i = index + 1; i < block.Statements.Count; i++)
        {
            var candidate = block.Statements[i];
            foreach (var invocation in candidate.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Execute" or "ExecuteAsync" } member) continue;
                if (model.GetSymbolInfo(member.Expression, cancellationToken).Symbol is not { } candidateSymbol) continue;
                if (!SymbolEqualityComparer.Default.Equals(candidateSymbol, receiverSymbol)) continue;

                execute = candidate;
                return DeclaredLocalIsUnusedBefore(model, statement, block, index, i, cancellationToken);
            }
        }

        return false;
    }

    private static bool DeclaredLocalIsUnusedBefore(
        SemanticModel model,
        StatementSyntax statement,
        BlockSyntax block,
        int from,
        int to,
        CancellationToken cancellationToken)
    {
        if (statement is not LocalDeclarationStatementSyntax local) return true; // nothing moves
        if (model.GetDeclaredSymbol(local.Declaration.Variables[0], cancellationToken) is not { } declared) return false;

        for (int i = from + 1; i <= to; i++)
        {
            foreach (var identifier in block.Statements[i].DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, declared)) return false;
            }
        }

        return true;
    }

    private static async Task<Document> CaptureAsync(
        Document document,
        SemanticModel model,
        StatementSyntax statement,
        InvocationExpressionSyntax queued,
        StatementSyntax execute,
        CancellationToken cancellationToken)
    {
        var name = UniqueName(model, statement, cancellationToken);

        // the queueing call stays exactly where it was - only the *waiting* moves, which is the whole point
        var capture = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(queued.WithoutTrivia())))))
            .WithTriviaFrom(statement)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var awaited = SyntaxFactory.AwaitExpression(SyntaxFactory.IdentifierName(name));

        // an expression statement becomes a bare "await pending;"; a declaration keeps its variable, which is
        // why it had to be safe to move (see DeclaredLocalIsUnusedBefore)
        StatementSyntax resumed = statement is LocalDeclarationStatementSyntax local
            ? local.WithDeclaration(local.Declaration.WithVariables(
                SyntaxFactory.SingletonSeparatedList(
                    local.Declaration.Variables[0].WithInitializer(SyntaxFactory.EqualsValueClause(awaited)))))
            : SyntaxFactory.ExpressionStatement(awaited);

        // Trivia copied from the Execute statement it lands beside, rather than left to the formatter: the
        // newline that separates two statements is the *trailing* trivia of the first, so a node built without
        // any would run straight into whatever follows it ("await pending; return value;" on one line).
        resumed = resumed
            .WithLeadingTrivia(execute.GetLeadingTrivia())
            .WithTrailingTrivia(execute.GetTrailingTrivia());

        // an editor rather than two root rewrites: the wait and the Execute are separate statements in the
        // same block, and InsertAfter puts the resumed wait beside Execute rather than nesting a new block
        // around it, which is what replacing the node outright would do
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(statement, capture);
        editor.InsertAfter(execute, resumed);
        return editor.GetChangedDocument();
    }

    /// <summary>A name for the captured task that is not already in scope at this point.</summary>
    private static string UniqueName(SemanticModel model, StatementSyntax statement, CancellationToken cancellationToken)
    {
        const string Preferred = "pending";
        var taken = model.LookupSymbols(statement.SpanStart);

        string candidate = Preferred;
        for (int suffix = 2; Contains(taken, candidate); suffix++)
        {
            candidate = Preferred + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate;

        static bool Contains(ImmutableArray<ISymbol> symbols, string name)
        {
            foreach (var symbol in symbols)
            {
                if (string.Equals(symbol.Name, name, System.StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
