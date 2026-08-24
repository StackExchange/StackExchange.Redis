using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// The compilation setup shared by <see cref="Verifier{TAnalyzer}"/> and
/// <see cref="CodeFixVerifier{TAnalyzer, TCodeFix}"/>.
/// </summary>
/// <remarks>
/// Shared rather than copied because the two must agree: a code-fix test that assembled its compilation even
/// slightly differently from the analyzer test would be verifying the fix against a different world than the
/// one the diagnostic came from.
/// </remarks>
internal static class TestSetup
{
    /// <summary>
    /// Test sources use string literals for keys/values, which trips the library's own <c>[Experimental]</c>
    /// gate on the implicit <c>string</c> -&gt; <c>RedisValue</c> conversion.
    /// </summary>
    /// <remarks>
    /// Unrelated to anything under test here, and it surfaces as an *error* in the test compilation, so it is
    /// opted out of for every case. Prepended to fixed sources too, since the harness compares them literally.
    /// </remarks>
    public const string Preamble = "#pragma warning disable StringToRedisValue";

    /// <summary>
    /// Reference assemblies matching the library build we load below.
    /// </summary>
    /// <remarks>
    /// The harness only ships well-known sets up to a point, and mismatching them against the
    /// StackExchange.Redis build we reference gives CS1705 (assembly wants a newer System.Runtime), so
    /// describe the current target explicitly rather than pinning to whatever the harness happens to know.
    /// </remarks>
    public static readonly ReferenceAssemblies Net10 = new(
        "net10.0",
        new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
        Path.Combine("ref", "net10.0"));

    /// <summary>Prefix a test source with <see cref="Preamble"/>.</summary>
    public static string WithPreamble(string source) => Preamble + System.Environment.NewLine + source;

    /// <summary>
    /// Apply the shared reference set and options to a test.
    /// </summary>
    /// <remarks>
    /// The analyzers here resolve StackExchange.Redis types by metadata name and do nothing at all when they
    /// are absent - so without <paramref name="referenceLibrary"/> the positive cases would fail to compile
    /// rather than silently pass, but the *negative* cases would trivially "pass" by finding no diagnostics.
    /// Hence both this and NoLibrary.cs, which asserts the absent case deliberately rather than by accident.
    /// </remarks>
    public static void Configure(AnalyzerTest<DefaultVerifier> test, bool referenceLibrary, string? minServerVersion)
    {
        test.ReferenceAssemblies = Net10;

        if (referenceLibrary)
        {
            test.TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(StackExchange.Redis.ConnectionMultiplexer).Assembly.Location));
        }

        if (minServerVersion is not null)
        {
            // written as a .globalconfig entry, which is also how the MSBuild property arrives once
            // CompilerVisibleProperty has translated it - so this covers both spellings' consumption path
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", SourceText.From(
                "is_global = true" + System.Environment.NewLine
                + "redis.min_server_version = " + minServerVersion + System.Environment.NewLine,
                Encoding.UTF8)));
        }
    }
}
