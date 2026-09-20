using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace StackExchange.Redis.Build;

/// <summary>
/// Points out redis calls made where a <c>CancellationToken</c> is in scope but cannot be honoured.
/// </summary>
/// <remarks>
/// <para>
/// <b>The signal is the token, not the call.</b> Most redis calls are perfectly fine; what is worth saying
/// something about is a method whose <i>signature promises cancellation</i> calling something that cannot
/// cancel. That promise is one the code cannot keep, and the failure is silent - the token is cancelled,
/// the caller waits anyway, and nothing in the source explains why.
/// </para>
/// <para>
/// <b>Scoped to the enclosing method's parameters</b>, rather than any token reachable anywhere. A token
/// parameter is a contract with the caller; a token in a field or a local may be plumbing that this call
/// was never meant to observe. Narrowing to the parameter keeps the rule quiet enough to leave on.
/// </para>
/// <para>
/// <b>A token being <i>reachable</i> is not the signal; a token being <i>promised</i> is.</b> That
/// distinction is what rules out the case this was nearly extended to cover: an async <c>[Fact]</c> has
/// an ambient <c>TestContext.Current.CancellationToken</c>, so by the "reachable" reading every test
/// calling redis would be flagged. But a test's token means "the run is being aborted", migrating a test
/// to a different surface is not a sensible response to that, and a redis call that ignores it finishes
/// in microseconds anyway. The rule would fire thousands of times in this repository alone and be
/// suppressed wholesale - and where an ambient test token genuinely matters, for long waits, xUnit's own
/// xUnit1051 already covers it.
/// </para>
/// <para>
/// Reported once per method rather than once per call. A repository method with a dozen redis calls has
/// one thing to decide, not a dozen; a diagnostic per call would be a wall of identical suggestions that
/// gets suppressed wholesale, which is the opposite of the intent.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.CancellationIgnored);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var redisAsync = start.Compilation.GetTypeByMetadataName("StackExchange.Redis.IRedisAsync");
            var token = start.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
            if (redisAsync is null || token is null) return;

            // NOTE: do not try to exclude "the context surface" by interface here. IDatabase itself
            // implements IRespTarget - it EXPOSES a context, it is not one - so excluding by that skips
            // exactly what this rule is for. The surfaces that accept a token are not IRedisAsync at all,
            // so the check below already passes over them.

            start.RegisterOperationBlockAction(block =>
            {
                if (block.OwningSymbol is not IMethodSymbol method) return;

                // a token PARAMETER is a contract with the caller; one in a field or a local may be
                // plumbing this call was never meant to observe
                var parameter = System.Array.Find(
                    method.Parameters.ToArray(),
                    p => SymbolEqualityComparer.Default.Equals(p.Type, token));
                if (parameter is null) return;

                foreach (var operation in block.OperationBlocks)
                {
                    if (TryFindCall(operation, redisAsync) is { } call)
                    {
                        // once per method: a repository method with a dozen redis calls has one thing to
                        // decide, not a dozen
                        block.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.CancellationIgnored,
                            call.Syntax.GetLocation(),
                            call.TargetMethod.Name,
                            parameter.Name));
                        return;
                    }
                }
            });
        });
    }

    /// <summary>The first call on a surface that cannot cancel, or null if there is none.</summary>
    private static IInvocationOperation? TryFindCall(IOperation root, INamedTypeSymbol redisAsync)
    {
        foreach (var descendant in root.DescendantsAndSelf())
        {
            if (descendant is not IInvocationOperation invocation) continue;

            var receiver = invocation.Instance?.Type ?? invocation.TargetMethod.ContainingType;
            if (!Implements(receiver, redisAsync)) continue;

            // a call that already takes a token is either on the new surface or explicitly opted in;
            // either way it is not what this rule is about
            if (invocation.TargetMethod.Parameters.Any(p => p.Type.Name == "CancellationToken")) continue;

            return invocation;
        }

        return null;
    }

    private static bool Implements(ITypeSymbol? type, INamedTypeSymbol? iface)
    {
        if (type is null || iface is null) return false;
        if (SymbolEqualityComparer.Default.Equals(type, iface)) return true;
        foreach (var candidate in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, iface)) return true;
        }

        return false;
    }
}
