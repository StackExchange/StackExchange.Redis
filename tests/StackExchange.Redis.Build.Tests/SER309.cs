using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Literal text in a RESP interpolated command, which the handler discards rather than sends.
/// </summary>
/// <remarks>
/// An error-severity rule, so the negative cases carry the weight: a false positive here is a broken build on
/// working code. The interesting negatives are the single space (deliberately allowed) and an ordinary
/// interpolated string that happens to be nearby, which must not be touched.
/// </remarks>
public class SER309 : Verifier<RespInterpolationAnalyzer>
{
    private const string Using = """
        #pragma warning disable SER010
        using StackExchange.Redis;
        using StackExchange.Redis.Interpolated;
        """;

    [Fact]
    public Task InlineToken_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Execute("SET", $"{key}{|#0: nx |}{value}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" nx "));

    [Fact]
    public Task TwoSpaces_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("GET", $"{key}{|#0:  |}{key}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments("  "));

    [Fact]
    public Task LeadingCommandName_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("GET", $"{|#0:SET |}{key}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments("SET "));

    [Fact]
    public Task EveryLiteralIsReportedSeparately() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Execute("SET", $"{key}{|#0: nx |}{value}{|#1: xx|}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" nx "),
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(1).WithArguments(" xx"));

    [Fact]
    public Task LeadingSpace_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("GET", $"{|#0: |}{key}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" "));

    [Fact]
    public Task TrailingSpace_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Execute("GET", $"{key}{|#0: |}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Error).WithLocation(0).WithArguments(" "));

    // ---- negatives ---------------------------------------------------------------------------------

    [Fact]
    public Task SingleSpace_IsAllowed() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Execute("SET", $"{key} {value}");
            }
        }
        """);

    [Fact]
    public Task NoLiterals_IsAllowed() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Execute("SET", $"{key}{value}");
            }
        }
        """);

    [Fact]
    public Task OrdinaryInterpolatedString_IsNotTouched() => VerifyAsync(
        Using + """
        class C
        {
            string M(RedisKey key) => $"the key is {key}, obviously";
        }
        """);

    [Fact]
    public Task InterpolatedStringForAnotherHandler_IsNotTouched() => VerifyAsync(
        Using + """
        using System.Text;
        class C
        {
            void M(StringBuilder sb, RedisKey key) => sb.Append($"key: {key} here");
        }
        """);
}
