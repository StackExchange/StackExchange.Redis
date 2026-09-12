using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace StackExchange.Redis.Build;

/// <summary>
/// Reports literal text inside a RESP interpolated command, which the handler discards rather than sends.
/// </summary>
/// <remarks>
/// <para>
/// The handler deliberately has no runtime check - <c>AppendLiteral</c> is empty, so the JIT can drop it -
/// which makes this rule the only thing standing between <c>$"{key} nx {val}"</c> and a command that quietly
/// omits <c>nx</c>. Hence error severity: the code cannot do what it plainly says.
/// </para>
/// <para>
/// Detection is by converted type rather than by <c>OperationKind.InterpolatedStringHandlerCreation</c>, which
/// keeps this working against the old Roslyn this assembly compiles against (see <c>RoslynShims</c>).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RespInterpolationAnalyzer : DiagnosticAnalyzer
{
    private const string HandlerTypeName = "StackExchange.Redis.Interpolated.RespCommandHandler";

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

        foreach (var content in node.Contents)
        {
            if (content is not InterpolatedStringTextSyntax text) continue;

            var value = text.TextToken.ValueText;
            if (value == " ") continue; // the one permitted separator

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
