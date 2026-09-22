using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Literal text in a RESP interpolated command, which is sent, but re-parsed and re-encoded every call.
/// </summary>
/// <remarks>
/// <para>
/// A <b>warning</b>, because this is a cost rather than a correctness problem - the text really is sent. The
/// negative cases still carry the weight: the interesting ones are the single space (deliberately allowed)
/// and an ordinary interpolated string that happens to be nearby, which must not be touched.
/// </para>
/// <para>
/// This summary said the opposite twice over - that the handler <i>discards</i> literals, and that the rule
/// is an error - which was true of an earlier builder whose <c>AppendLiteral</c> was empty. The same pair of
/// stale claims was corrected on the analyzer itself and left standing here.
/// </para>
/// </remarks>
public class SER309 : Verifier<RespInterpolationAnalyzer>
{
    private const string Using = """
        using StackExchange.Redis;
        """;

    [Fact]
    public Task InlineToken_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Render("SET", $"{key}{|#0: nx |}{value}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" nx "));

    [Fact]
    public Task TwoSpaces_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Render("GET", $"{key}{|#0:  |}{key}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("  "));

    [Fact]
    public Task LeadingCommandName_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Render("GET", $"{|#0:SET |}{key}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("SET "));

    [Fact]
    public Task EveryLiteralIsReportedSeparately() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Render("SET", $"{key}{|#0: nx |}{value}{|#1: xx|}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" nx "),
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(1).WithArguments(" xx"));

    [Fact]
    public Task LeadingSpace_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Render("GET", $"{|#0: |}{key}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" "));

    [Fact]
    public Task TrailingSpace_IsFlagged() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Render("GET", $"{key}{|#0: |}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" "));

    // ---- negatives ---------------------------------------------------------------------------------

    [Fact]
    public Task SingleSpace_IsAllowed() => VerifyAsync(
        Using + """
        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Render("SET", $"{key} {value}");
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
                using var frame = ctx.Render("SET", $"{key}{value}");
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
