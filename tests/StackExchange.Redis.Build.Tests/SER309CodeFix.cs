using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StackExchange.Redis.CodeFixes;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// The fix for SER309: replace an inline token with a hole referencing a declared <c>[Resp]</c> fragment.
/// </summary>
/// <remarks>
/// This is what makes rejecting inline tokens tolerable rather than merely strict - you write the command the
/// way it reads and take the fix, so the committed code is the strict form without anyone having to remember
/// the member name.
/// </remarks>
public class SER309CodeFix : CodeFixVerifier<RespInterpolationAnalyzer, RespLiteralCodeFixProvider>
{
    // Both halves are spelled out because the code-fix harness runs analyzers, not generators; in real use
    // the bodies come from RespFragmentGenerator. The fixer only looks for a [Resp] property, so this is
    // faithful to what it actually resolves against.
    private const string Declarations = """
        #pragma warning disable SER010, SER011
        using StackExchange.Redis;
        using StackExchange.Redis.Interpolated;

        internal static partial class RespLiterals
        {
            [Resp]
            internal static partial RespFragment Nx { get; }

            [Resp("SETINFO", "lib-name")]
            internal static partial RespFragment SetInfoLibName { get; }
        }

        internal static partial class RespLiterals
        {
            internal static partial RespFragment Nx => new("$2\r\nNX\r\n"u8);

            internal static partial RespFragment SetInfoLibName => new("$7\r\nSETINFO\r\n$8\r\nlib-name\r\n"u8, 2);
        }
        """;

    [Fact]
    public Task InlineToken_IsReplacedWithTheDeclaredFragment() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Execute("SET", $"{key} {value}{|#0: nx|}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Execute("SET", $"{key} {value} {RespLiterals.Nx}");
            }
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" nx"));

    [Fact]
    public Task SeparatorsAreKeptOnBothSides() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Execute("SET", $"{key}{|#0: nx |}{value}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Execute("SET", $"{key} {RespLiterals.Nx} {value}");
            }
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" nx "));

    [Fact]
    public Task MatchingIsCaseInsensitive() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("GET", $"{key}{|#0: NX|}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("GET", $"{key} {RespLiterals.Nx}");
            }
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" NX"));

    // ---- cases with no fix -------------------------------------------------------------------------

    [Fact]
    public Task UndeclaredToken_OffersNothing() => VerifyNoFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("GET", $"{key}{|#0: withsave|}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" withsave"));

    [Fact]
    public Task MultiTokenFragment_DoesNotMatchOneInlineToken() => VerifyNoFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("CLIENT", $"{key}{|#0: SETINFO|}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" SETINFO"));

    [Fact]
    public Task RunOfSeveralTokens_OffersNothing() => VerifyNoFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("GET", $"{key}{|#0: nx xx|}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" nx xx"));
}
