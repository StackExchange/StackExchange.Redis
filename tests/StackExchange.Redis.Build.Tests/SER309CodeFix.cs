using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using StackExchange.Redis.CodeFixes;
using StackExchange.Redis.Protocol;
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
        #pragma warning disable SER011
        using StackExchange.Redis;

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

    /// <summary>
    /// The body a declared fragment gets from RespFragmentGenerator, which this harness does not run - so a
    /// fix that declares one legitimately leaves the partial property unimplemented here.
    /// </summary>
    private static DiagnosticResult MissingGeneratedBody(string property)
        => DiagnosticResult.CompilerError("CS9248").WithLocation(1).WithArguments(property);

    [Fact]
    public Task InlineToken_IsReplacedWithTheDeclaredFragment() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Raw.Execute("SET", $"{key} {value}{|#0: nx|}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Raw.Execute("SET", $"{key} {value} {RespLiterals.Nx}");
            }
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" nx"));

    [Fact]
    public Task SeparatorsAreKeptOnBothSides() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Raw.Execute("SET", $"{key}{|#0: nx |}{value}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Raw.Execute("SET", $"{key} {RespLiterals.Nx} {value}");
            }
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" nx "));

    [Fact]
    public Task MatchingIsCaseInsensitive() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("GET", $"{key}{|#0: NX|}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("GET", $"{key} {RespLiterals.Nx}");
            }
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" NX"));

    // ---- cases with no fix -------------------------------------------------------------------------

    [Fact]
    public Task UndeclaredToken_IsDeclaredInTheContainingType() => VerifyFixAsync(
        Declarations + """

        partial class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("GET", $"{key}{|#0: withsave|}");
            }
        }
        """,
        Declarations + """

        partial class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("GET", $"{key} {Withsave}");
            }

            [Resp]
            private static partial RespFragment {|#1:Withsave|} { get; }
        }
        """,
        0,
        [Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" withsave")],
        MissingGeneratedBody("C.Withsave"));

    [Fact]
    public Task DeclaringAHyphenatedTokenKeepsItVerbatim() => VerifyFixAsync(
        Declarations + """

        partial class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("CLIENT", $"{key}{|#0: lib-ver|}");
            }
        }
        """,
        Declarations + """

        partial class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("CLIENT", $"{key} {LibVer}");
            }

            [Resp("lib-ver")]
            private static partial RespFragment {|#1:LibVer|} { get; }
        }
        """,
        0,
        [Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" lib-ver")],
        MissingGeneratedBody("C.LibVer"));

    [Fact]
    public Task MultiTokenFragment_DoesNotMatchOneInlineToken() => VerifyFixAsync(
        Declarations + """

        partial class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("CLIENT", $"{key}{|#0: SETINFO|}");
            }
        }
        """,
        // SetInfoLibName exists but spans two tokens, so it is not a match for this one; the declare fix is
        // offered instead, which is the right answer - CLIENT SETINFO alone is a different fragment
        Declarations + """

        partial class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("CLIENT", $"{key} {Setinfo}");
            }

            [Resp]
            private static partial RespFragment {|#1:Setinfo|} { get; }
        }
        """,
        0,
        [Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" SETINFO")],
        MissingGeneratedBody("C.Setinfo"));

    [Fact]
    public Task RunOfSeveralTokens_OffersNothing() => VerifyNoFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key)
            {
                using var frame = ctx.Raw.Execute("GET", $"{key}{|#0: nx xx|}");
            }
        }
        """,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" nx xx"));

    // NOTE this harness compiles against the PUBLIC surface, with no InternalsVisibleTo - so it is an
    // external caller, and RedisCommand is genuinely out of reach. That is the half of the fork worth
    // testing here: even for a command the library knows, an outside caller gets the field, because the
    // enum it would otherwise use is internal.
    [Fact]
    public Task LeadingCommand_KnownName_ExternallyDeclaresAField() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Raw.Execute($"{|#0:SET |}{key}{value}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Raw.Execute($"{SetCommand} {key}{value}");
            }

            private static readonly RespCommand SetCommand = "SET".Command(preform: true);
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("SET "));

    [Fact]
    public Task LeadingCommand_UnknownName_DeclaresAPreformedField() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisValue value)
            {
                using var frame = ctx.Raw.Execute($"{|#0:FT.SEARCH |}{value}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisValue value)
            {
                using var frame = ctx.Raw.Execute($"{FtSearchCommand} {value}");
            }

            private static readonly RespCommand FtSearchCommand = "FT.SEARCH".Command(preform: true);
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("FT.SEARCH "));

    [Fact]
    public Task NonLeadingToken_StillOffersTheFragmentFix() => VerifyFixAsync(
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Raw.Execute("SET", $"{key}{value}{|#0: nx|}");
            }
        }
        """,
        Declarations + """

        class C
        {
            void M(RespContext ctx, RedisKey key, RedisValue value)
            {
                using var frame = ctx.Raw.Execute("SET", $"{key}{value} {RespLiterals.Nx}");
            }
        }
        """,
        0,
        Diagnostic("SER309", DiagnosticSeverity.Warning).WithLocation(0).WithArguments(" nx"));
}
