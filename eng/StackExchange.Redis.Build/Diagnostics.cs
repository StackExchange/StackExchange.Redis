using Microsoft.CodeAnalysis;

namespace StackExchange.Redis.Build;

/// <summary>
/// Diagnostics reported by the analyzers and generators shipped inside the StackExchange.Redis package.
/// </summary>
/// <remarks>
/// <para>
/// The <c>SER</c> identifier space is shared with the <c>[Experimental]</c> API gates in
/// <c>RESPite.Experiments</c>, which own <c>SER0xx</c> and mean something quite different ("this API is
/// preview"). Everything reported by <em>this</em> assembly lives in <c>SER3xx</c>, split as:
/// </para>
/// <list type="bullet">
/// <item><description><c>SER300</c>-<c>SER349</c>: usage guidance about consumer code (the analyzers).</description></item>
/// <item><description><c>SER350</c>-<c>SER399</c>: build-level problems (the generators).</description></item>
/// </list>
/// <para>
/// These are a public contract: once shipped, an ID cannot be reused or re-pointed, because consumers put
/// them in <c>NoWarn</c> and <c>.editorconfig</c>. Analyzer rules default to <see
/// cref="DiagnosticSeverity.Info"/> - the code they flag is correct, just not optimal, and a shipped warning
/// would break builds that set <c>TreatWarningsAsErrors</c>.
/// </para>
/// </remarks>
internal static class Diagnostics
{
    private const string UsageCategory = "Usage", BuildCategory = "Build";

    /// <summary>
    /// Where the docs for a rule live; <c>docs/rules/{id}.md</c> on the published site.
    /// </summary>
    /// <remarks>
    /// Separate from the <c>exp/</c> pages used by the <c>[Experimental]</c> gates, which mean something else
    /// entirely ("this API is preview"). Every ID below must have a page, because the message can only carry a
    /// sketch of the rewrite - the caveats that actually catch people out (the result changes meaning, the
    /// queued task disappears, CommandFlags has to be carried over) only fit in prose.
    /// </remarks>
    private const string HelpLinkFormat = "https://stackexchange.github.io/StackExchange.Redis/rules/{0}";

    /// <summary>
    /// Family A: the condition duplicates a <c>when:</c> argument that already exists on the queued command.
    /// </summary>
    /// <remarks>
    /// The cheapest and safest family: a purely mechanical rewrite that needs no newer server, because the
    /// conditional form has existed as long as the command has. Kept separate from <see
    /// cref="PreferNewerAtomicOperation"/> precisely because that one is version-dependent and this is not.
    /// </remarks>
    public static readonly DiagnosticDescriptor PreferConditionalArgument = new(
        id: "SER300",
        title: "Transaction can be replaced by a conditional argument",
        messageFormat: "This transaction ({0} guarding {1}) can be expressed as {2} - the condition duplicates an argument the command already has",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A transaction whose only purpose is to make one operation conditional can be replaced by the command's own conditional argument, which is a single round-trip and cannot abort under contention.",
        helpLinkUri: HelpLink("SER300"));

    /// <summary>
    /// Family B: a newer single command subsumes both the condition and the write.
    /// </summary>
    /// <remarks>
    /// Separate ID from <see cref="PreferConditionalArgument"/> because the suggestion is only actionable
    /// against a new enough server (compare-and-set needs 8.4 - see <c>RedisFeatures.SetWithValueCheck</c> and
    /// <c>DeleteWithValueCheck</c>), and an analyzer cannot see the server it will talk to. A consumer stuck on
    /// an older server wants to silence this one while keeping SER300, which a shared ID would prevent. This is
    /// also why the library's own compatibility fallbacks suppress it rather than being rewritten.
    /// <para>
    /// The required version is per-mapping data rather than part of the rule (see <c>ServerVersion</c>), so the
    /// message can name it and a project that declares its own floor - <c>Redis_MinServerVersion</c>, or
    /// <c>redis.min_server_version</c> in <c>.editorconfig</c> - gets only the suggestions it can act on.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor PreferNewerAtomicOperation = new(
        id: "SER301",
        title: "Transaction can be replaced by a single atomic operation",
        messageFormat: "This transaction ({0} guarding {1}) can be expressed as {2}, which is atomic on the server and needs no WATCH (requires server {3} or later)",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A transaction implementing compare-and-set can be replaced by the equivalent conditional command on servers that support it, which is a single round-trip and cannot abort under contention.",
        helpLinkUri: HelpLink("SER301"));

    /// <summary>
    /// The generated code cannot be compiled at the language version in effect, so nothing was generated.
    /// </summary>
    /// <remarks>
    /// Expected to be rare, and always fixable by the consumer with <c>&lt;LangVersion&gt;</c> - the language
    /// version is not tied to the target framework, so an old TFM is not a barrier on a current SDK. A warning
    /// rather than info even so, because it cannot fire spuriously (we know the language version, and only
    /// look when <c>[AsciiHash]</c> is actually used) and the alternatives are both worse: errors inside
    /// generated code, or an unexplained "partial method has no implementing declaration".
    /// </remarks>
    public static readonly DiagnosticDescriptor LanguageVersionTooLow = new(
        id: "SER350",
        title: "Language version too low for generated code",
        messageFormat: "'{0}' requires C# {1} or later, but this project uses C# {2}; no code was generated. Raise <LangVersion> to use this feature.",
        category: BuildCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("SER350"));

    private static string HelpLink(string id) => string.Format(HelpLinkFormat, id);
}
