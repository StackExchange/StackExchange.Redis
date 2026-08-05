using System.IO;
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
public abstract class Verifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    /// <summary>
    /// Reference assemblies matching the library build we load below.
    /// </summary>
    /// <remarks>
    /// The harness only ships well-known sets up to a point, and mismatching them against the
    /// StackExchange.Redis build we reference gives CS1705 (assembly wants a newer System.Runtime), so
    /// describe the current target explicitly rather than pinning to whatever the harness happens to know.
    /// </remarks>
    private static readonly ReferenceAssemblies Net10 = new(
        "net10.0",
        new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
        Path.Combine("ref", "net10.0"));

    /// <summary>Expect a diagnostic with this id at the marked location.</summary>
    protected static DiagnosticResult Diagnostic(string id, DiagnosticSeverity severity = DiagnosticSeverity.Info)
        => new(id, severity);

    /// <summary>Verify that <paramref name="source"/> produces exactly <paramref name="expected"/>.</summary>
    protected static Task VerifyAsync(string source, params DiagnosticResult[] expected)
        => RunAsync(source, referenceLibrary: true, expected);

    /// <summary>
    /// As <see cref="VerifyAsync"/>, but with no reference to StackExchange.Redis at all.
    /// </summary>
    /// <remarks>
    /// For asserting the no-op path: the analyzer resolves its types by metadata name and does nothing when
    /// they are absent, and that has to keep working (and not throw) in the overwhelming majority of
    /// compilations, which have never heard of this library.
    /// </remarks>
    protected static Task VerifyWithoutLibraryAsync(string source)
        => RunAsync(source, referenceLibrary: false);

    private static Task RunAsync(string source, bool referenceLibrary, params DiagnosticResult[] expected)
    {
        // Test sources use string literals for keys/values, which trips the library's own [Experimental]
        // gate on the implicit string -> RedisValue conversion. That is unrelated to what we are testing, and
        // surfaces as an error in the test compilation, so opt out of it for every case.
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = "#pragma warning disable StringToRedisValue" + System.Environment.NewLine + source,
            ReferenceAssemblies = Net10,
        };

        // The analyzer resolves StackExchange.Redis.Condition by metadata name and does nothing at all if it
        // is absent - so without this reference the positive cases would fail to compile rather than silently
        // pass, but the *negative* cases would trivially "pass" by finding no diagnostics. Hence both this and
        // NoLibrary.cs, which asserts the absent case deliberately rather than by accident.
        if (referenceLibrary)
        {
            test.TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(StackExchange.Redis.ConnectionMultiplexer).Assembly.Location));
        }

        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
