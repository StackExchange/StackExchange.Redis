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
/// them in <c>NoWarn</c> and <c>.editorconfig</c>.
/// </para>
/// <para>
/// Everything here defaults to <see cref="DiagnosticSeverity.Warning"/>, including the usage rules, whose code
/// is correct rather than broken. That is a deliberate change from an earlier <see
/// cref="DiagnosticSeverity.Info"/> default: information-level diagnostics are not printed by <c>dotnet
/// build</c> at all, so outside an IDE the rules simply did not exist, and a suggestion nobody sees is not
/// worth shipping. The cost is real and should be understood rather than discovered: a consumer building with
/// <c>TreatWarningsAsErrors</c> gets a *failing build* on upgrade, on code that works. They can turn any of
/// these down per-rule in <c>.editorconfig</c> or <c>NoWarn</c>, and the help pages say how - but the first
/// experience is a broken build, and that is the trade being made on purpose.
/// </para>
/// <para>
/// Which is also why the usage rules hedge - "may be replaceable", "looks like", "consider" - rather than
/// asserting. They are heuristics over source text, so a false positive is rare rather than impossible, and
/// arriving as a warning already overstates the case; wording them as findings of fact would overstate it
/// twice. The build-level <see cref="LanguageVersionTooLow"/> does not hedge, because it is not guessing.
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
        title: "Transaction may be replaceable by a conditional argument",
        messageFormat: "Consider expressing this transaction ({0} guarding {1}) as {2} - the condition duplicates an argument the command already has",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A transaction whose only purpose is to make one operation conditional can usually be replaced by the command's own conditional argument, which is a single round-trip and cannot abort under contention.",
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
    /// message can name it and a project that declares its own floor - <c>&lt;RedisMinServerVersion&gt;</c>, or
    /// <c>redis.min_server_version</c> in <c>.editorconfig</c> - gets only the suggestions it can act on.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor PreferNewerAtomicOperation = new(
        id: "SER301",
        title: "Transaction may be replaceable by a single atomic operation",
        messageFormat: "Consider expressing this transaction ({0} guarding {1}) as {2}, which is atomic on the server and needs no WATCH (requires server {3} or later)",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A transaction implementing compare-and-set can usually be replaced by the equivalent conditional command on servers that support it, which is a single round-trip and cannot abort under contention.",
        helpLinkUri: HelpLink("SER301"));

    /// <summary>
    /// Family C: the condition asks what the queued command already answers.
    /// </summary>
    /// <remarks>
    /// Its own ID rather than sharing <see cref="PreferConditionalArgument"/> because the fix is a different
    /// shape: it deletes the transaction instead of moving an argument into the command, and what the caller
    /// observes changes meaning - <c>Execute()</c> returning <c>false</c> ("the guard failed, nothing ran")
    /// becomes the command's own <c>false</c> ("it ran and had no effect"). Those coincide in intent but a
    /// caller distinguishing them wants to notice. Version-free: these return values have always been there.
    /// </remarks>
    public static readonly DiagnosticDescriptor RedundantCondition = new(
        id: "SER302",
        title: "Transaction condition may be redundant",
        messageFormat: "This transaction ({0} guarding {1}) looks redundant - consider {2}",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A condition that checks what the queued command already reports through its return value usually buys nothing: the transaction costs an extra round-trip and can abort, and the command alone says whether it acted.",
        helpLinkUri: HelpLink("SER302"));

    /// <summary>
    /// Family D: no condition at all - two queued commands that are one compound command.
    /// </summary>
    /// <remarks>
    /// The message assembles its own version clause (argument 3) rather than baking one into the format,
    /// because unlike <see cref="PreferNewerAtomicOperation"/> the requirement genuinely varies across this
    /// family - SMOVE is as old as sets, HGETDEL is 8.0 - and "requires server 1.0 or later" would be noise.
    /// </remarks>
    public static readonly DiagnosticDescriptor PreferCompoundCommand = new(
        id: "SER303",
        title: "Transaction may be replaceable by a single compound command",
        messageFormat: "These two queued operations ({0} then {1}) look like one command - consider {2}{3}",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A transaction used only to make two operations atomic can usually be replaced by the single command that does both, which is one round-trip and cannot abort.",
        helpLinkUri: HelpLink("SER303"));

    /// <summary>
    /// Family D, second flavour: the same command queued repeatedly, where one variadic call does the lot.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PreferCompoundCommand"/> because the result changes *shape* rather than just
    /// meaning: N calls each returning <c>bool</c> become one returning a count, and N returning a value become
    /// one returning an array. Somebody happy to adopt GETDEL may well not want to rework how they read results,
    /// and a shared ID would not let them separate the two.
    /// </remarks>
    public static readonly DiagnosticDescriptor PreferVariadicOverload = new(
        id: "SER304",
        title: "Repeated queued operations may suit the variadic overload",
        messageFormat: "These {1} queued {0} calls look like one command - consider {2}{3}",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same command queued several times over can usually be a single variadic call, which is one round-trip and needs no transaction to be atomic.",
        helpLinkUri: HelpLink("SER304"));

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
