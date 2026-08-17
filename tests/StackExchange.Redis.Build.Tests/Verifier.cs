using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Base for analyzer verification, in the shape used by DapperAOT: a source string with <c>{|#0:...|}</c>
/// markers, plus the diagnostics expected at those locations.
/// </summary>
/// <remarks>
/// The compilation itself is assembled by <see cref="TestSetup"/>, shared with
/// <see cref="CodeFixVerifier{TAnalyzer, TCodeFix}"/>.
/// </remarks>
public abstract class Verifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    /// <summary>Expect a diagnostic with this id at the marked location.</summary>
    /// <remarks>
    /// Defaults to <see cref="DiagnosticSeverity.Warning"/> because that is what most of the rules ship as; the
    /// harness checks severity, so this is also what stops a default being changed without anyone noticing.
    /// </remarks>
    protected static DiagnosticResult Diagnostic(string id, DiagnosticSeverity severity = DiagnosticSeverity.Warning)
        => new(id, severity);

    /// <summary>Verify that <paramref name="source"/> produces exactly <paramref name="expected"/>.</summary>
    protected static Task VerifyAsync(string source, params DiagnosticResult[] expected)
        => RunAsync(source, referenceLibrary: true, minServerVersion: null, expected);

    /// <summary>
    /// As <see cref="VerifyAsync"/>, but with the project declaring a minimum server version.
    /// </summary>
    protected static Task VerifyWithMinServerVersionAsync(string source, string minServerVersion, params DiagnosticResult[] expected)
        => RunAsync(source, referenceLibrary: true, minServerVersion, expected);

    /// <summary>
    /// As <see cref="VerifyAsync"/>, but with no reference to StackExchange.Redis at all.
    /// </summary>
    /// <remarks>
    /// For asserting the no-op path: the analyzer resolves its types by metadata name and does nothing when
    /// they are absent, and that has to keep working (and not throw) in the overwhelming majority of
    /// compilations, which have never heard of this library.
    /// </remarks>
    protected static Task VerifyWithoutLibraryAsync(string source)
        => RunAsync(source, referenceLibrary: false, minServerVersion: null);

    private static Task RunAsync(string source, bool referenceLibrary, string? minServerVersion, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = TestSetup.WithPreamble(source),
        };

        TestSetup.Configure(test, referenceLibrary, minServerVersion);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
