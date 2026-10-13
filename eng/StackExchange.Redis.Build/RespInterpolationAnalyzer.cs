using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace StackExchange.Redis.Build;

/// <summary>
/// Reports literal text inside a RESP interpolated command, which is resolved afresh on every call.
/// </summary>
/// <remarks>
/// <para>
/// <b>The text IS sent</b> - <c>AppendLiteral</c> tokenizes it into arguments, with a leading token taken
/// as the command - so this is a cost, not a correctness problem, and the severity is warning. A token
/// written as a literal is parsed and UTF-8 encoded on every call, where a <c>[Resp]</c> fragment or a
/// <c>.Command()</c> field is prepared once. A single space between holes is a separator and is exempt.
/// </para>
/// <para>
/// <b>This description was wrong for a while, and the way it was wrong is worth keeping.</b> It used to
/// say the handler <i>discarded</i> literals and that the rule carried error severity - true of an earlier
/// builder whose <c>AppendLiteral</c> was empty, and left behind when that changed. A stale comment on an
/// analyzer is worse than none: it tells a reader the opposite of what the rule does.
/// </para>
/// <para>
/// Detection is by converted type rather than by <c>OperationKind.InterpolatedStringHandlerCreation</c>, which
/// keeps this working against the old Roslyn this assembly compiles against (see <c>RoslynShims</c>).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RespInterpolationAnalyzer : DiagnosticAnalyzer
{
    // The interpolated-string handler's full name, matched against the CONVERTED type of the expression.
    // It is a string rather than a symbol lookup, which makes it silently fragile: rename the type and this
    // analyzer stops reporting rather than stops compiling. It has been wrong twice - once when
    // RespCommandHandler became RespRequestBuilder, once when the namespace lost its .Interpolated.
    //
    // This used to claim "RespInterpolationAnalyzerTests pins the name against the real assembly", and no
    // such test has ever existed - which is how RespLiteralCodeFixProvider then went stale the same way,
    // pointing at three .Interpolated names for months. What actually pins it is SER309 and SER309CodeFix
    // in StackExchange.Redis.Build.Tests, whose snippets compile against the real library, and those now
    // run in CI - which they did not when this rotted.
    private const string HandlerTypeName = "StackExchange.Redis.Protocol.RespRequestBuilder";

    /// <summary>The trimmed token, handed to the code fix so it need not re-parse the literal.</summary>
    public const string TokenProperty = "Token";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Diagnostics.RespLiteralNotSent);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // the overwhelming majority of compilations have never heard of this library; resolve once and
            // do nothing at all when it is absent
            var handler = start.Compilation.GetTypeByMetadataName(HandlerTypeName);
            if (handler is null) return;

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, handler),
                SyntaxKind.InterpolatedStringExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol handler)
    {
        var node = (InterpolatedStringExpressionSyntax)context.Node;
        var converted = context.SemanticModel.GetTypeInfo(node, context.CancellationToken).ConvertedType;
        if (!SymbolEqualityComparer.Default.Equals(converted, handler)) return;

        var contents = node.Contents;
        for (var i = 0; i < contents.Count; i++)
        {
            if (contents[i] is not InterpolatedStringTextSyntax text) continue;

            var value = text.TextToken.ValueText;

            // a single space is permitted, but only BETWEEN holes: a leading or trailing one separates
            // nothing, and satisfying "exactly one space" is not the same as being a separator
            if (value == " " && i > 0 && i < contents.Count - 1) continue;

            var token = value.Trim();
            var properties = ImmutableDictionary<string, string?>.Empty.Add(TokenProperty, token);
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.RespLiteralNotSent,
                text.GetLocation(),
                properties,
                value));
        }
    }
}
