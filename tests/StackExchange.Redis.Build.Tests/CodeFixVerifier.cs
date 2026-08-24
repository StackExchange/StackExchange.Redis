using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Base for code-fix verification: the source with its markers, the diagnostics expected, and the source the
/// fix is expected to produce.
/// </summary>
/// <remarks>
/// The fixed source is compared literally, so these tests pin the emitted *formatting* as well as the rewrite.
/// That is deliberate - a fix that produces correct but mangled code is a fix people undo - but it does mean a
/// whitespace change in the fixer shows up as a test failure to read rather than a mystery.
/// </remarks>
public abstract class CodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    /// <summary>The fix offered first: discard the queued result.</summary>
    protected const int DiscardFix = 0;

    /// <summary>The fix offered second: capture the task and await it after Execute.</summary>
    protected const int CaptureFix = 1;

    /// <inheritdoc cref="Verifier{TAnalyzer}.Diagnostic"/>
    protected static DiagnosticResult Diagnostic(string id, DiagnosticSeverity severity = DiagnosticSeverity.Warning)
        => new(id, severity);

    /// <summary>
    /// Verify that applying <paramref name="codeActionIndex"/> to <paramref name="source"/> yields
    /// <paramref name="fixedSource"/>.
    /// </summary>
    protected static Task VerifyFixAsync(string source, string fixedSource, int codeActionIndex, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = TestSetup.WithPreamble(source),
            FixedCode = TestSetup.WithPreamble(fixedSource),
            CodeActionIndex = codeActionIndex,
        };

        TestSetup.Configure(test, referenceLibrary: true, minServerVersion: null);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verify that no fix is offered for <paramref name="source"/>, which still reports
    /// <paramref name="expected"/>.
    /// </summary>
    /// <remarks>
    /// Expressed as "the code is unchanged by fixing it", which is how the harness spells "nothing on offer".
    /// Worth asserting explicitly: the shapes with no safe rewrite are a decision, and a fix quietly appearing
    /// for one of them later would be a rewrite nobody reasoned about.
    /// </remarks>
    protected static Task VerifyNoFixAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = TestSetup.WithPreamble(source),
            FixedCode = TestSetup.WithPreamble(source),
        };

        TestSetup.Configure(test, referenceLibrary: true, minServerVersion: null);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
