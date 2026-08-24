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
/// Everything here defaults to <see cref="DiagnosticSeverity.Warning"/> or above, including the suggestion
/// rules, whose code is correct rather than broken. That is a deliberate change from an earlier <see
/// cref="DiagnosticSeverity.Info"/> default: information-level diagnostics are not printed by <c>dotnet
/// build</c> at all, so outside an IDE the rules simply did not exist, and a suggestion nobody sees is not
/// worth shipping. The cost is real and should be understood rather than discovered: a consumer building with
/// <c>TreatWarningsAsErrors</c> gets a *failing build* on upgrade, on code that works. They can turn any of
/// these down per-rule in <c>.editorconfig</c> or <c>NoWarn</c>, and the help pages say how - but the first
/// experience is a broken build, and that is the trade being made on purpose.
/// </para>
/// <para>
/// Which is also why the *suggestion* rules hedge - "may be replaceable", "looks like", "consider" - rather
/// than asserting. They are heuristics over source text, so a false positive is rare rather than impossible,
/// and arriving as a warning already overstates the case; wording them as findings of fact would overstate it
/// twice.
/// </para>
/// <para>
/// Two rules here are not of that kind and are worded flatly, because they are not guessing: the build-level
/// <see cref="LanguageVersionTooLow"/>, and <see cref="AwaitBeforeExecute"/> - the only <see
/// cref="DiagnosticSeverity.Error"/> in the set - which describes code that cannot work rather than code that
/// could be better. Severity here is a statement about certainty, so keep the two kinds apart when adding an
/// ID: an error that can be wrong is a broken build on correct code, which is a far worse trade than a
/// warning that can be wrong.
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
    private const string HelpLinkFormat = "https://seredis.dev/rules/{0}";

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
    /// Waiting on a command queued to a transaction or batch, before anything has been sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one <see cref="DiagnosticSeverity.Error"/> in the set, and the only rule here that describes broken
    /// code rather than improvable code. A command queued on an <c>ITransaction</c>/<c>IBatch</c> is not sent
    /// until <c>Execute[Async]</c>, so the task it hands back cannot complete before then: awaiting it at the
    /// point of queueing deadlocks the caller for good.
    /// </para>
    /// <para>
    /// It can be an error without hedging because there is no arrangement of the surrounding code that makes it
    /// work. The wait sits at the queueing site, so even a prior <c>Execute</c> is no help - *this* command was
    /// queued after it and will never be sent. That is what makes it flow-insensitive, and so certain; the
    /// shapes that would need ordering to judge (a task captured to a local, or gathered by <c>Task.WhenAll</c>,
    /// and then awaited) are deliberately not reported, because awaiting a captured task *after* <c>Execute</c>
    /// is the recommended fix and getting the order wrong would error on correct code.
    /// </para>
    /// <para>
    /// <see cref="AwaitFireAndForgetResult"/> is the one case that survives the wait, and is split off rather
    /// than excluded silently.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor AwaitBeforeExecute = new(
        id: "SER305",
        title: "Waiting for a queued command before it is executed will never complete",
        messageFormat: "'{0}' is queued on {1} and is not sent until Execute[Async](), so waiting for its result here will never complete; capture the task and await it after Execute[Async](), or discard it",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Commands queued on a transaction or batch are only sent when Execute[Async]() is called, so the task returned at the point of queueing cannot complete until then; awaiting or blocking on it there deadlocks.",
        helpLinkUri: HelpLink("SER305"));

    /// <summary>
    /// As <see cref="AwaitBeforeExecute"/>, but with <c>CommandFlags.FireAndForget</c>, which does not deadlock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fire-and-forget hands back an already-completed task carrying the default value (see
    /// <c>RedisTransaction.ExecuteAsync</c>), so this one is legal: it returns immediately, and the command is
    /// still queued and sent by <c>Execute</c> like any other. What it cannot do is produce the server's answer
    /// - not later, not after <c>Execute</c>, not ever - so the value read from it is always <c>default</c>.
    /// </para>
    /// <para>
    /// Hence a warning rather than an error, and its own ID: somebody using fire-and-forget deliberately needs
    /// to be able to silence this without silencing <see cref="AwaitBeforeExecute"/>, which is the one that says
    /// their code cannot work. It is also the only rule here whose advice is *only* "discard it" - capturing the
    /// task to await after <c>Execute</c> is the fix for the other one and gains nothing at all here.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor AwaitFireAndForgetResult = new(
        id: "SER306",
        title: "Waiting for a fire-and-forget result yields nothing",
        messageFormat: "'{0}' is fire-and-forget, so its result is the default value whenever it is read, not the server's answer; discard it, or drop the fire-and-forget flag if you want the result",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A fire-and-forget command completes immediately with the default value and never carries a result from the server, so awaiting or blocking on it reads nothing meaningful.",
        helpLinkUri: HelpLink("SER306"));

    /// <summary>
    /// Blocking a thread on a redis call instead of awaiting it - "sync over async".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this leads to is systemic rather than local, which is why the rule is worth having: the
    /// blocked thread is holding a thread-pool thread hostage while it waits for a reply that *also* needs a
    /// thread-pool thread to be processed. Enough of those and the pool cannot make progress at all, and the
    /// symptom - timeouts, with data sitting unread in the socket - looks nothing like its cause.
    /// </para>
    /// <para>
    /// The help link goes to the explainer rather than to a rule page, deliberately: what people need here is
    /// the thread-pool story, not a description of the squiggle. Fire-and-forget is excluded, since its task is
    /// already complete and blocking on it does not wait for anything - that is
    /// <see cref="AwaitFireAndForgetResult"/>'s business instead.
    /// </para>
    /// <para>
    /// The message says "await it instead" and stops there, deliberately. The synchronous API is *not* the
    /// answer: it is not literally sync-over-async, but a blocked thread is a blocked thread, and if the caller
    /// is on the thread-pool - which it is in ASP.NET and anything else pool-driven - the starvation is
    /// identical. An analyzer that pointed people at it would be sending them somewhere just as bad.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BlockingOnRedisCall = new(
        id: "SER307",
        title: "Blocking on a redis call instead of awaiting it",
        messageFormat: "'{0}' is being waited on synchronously, which ties up a thread until redis replies - and the reply needs a thread of its own to be processed; await it instead",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Blocking on an asynchronous redis call holds a thread-pool thread while waiting for a reply whose processing also needs the thread-pool; enough of these will starve the pool, at which point replies cannot be processed at all.",
        helpLinkUri: "https://seredis.dev/SyncOverAsync");

    /// <summary>
    /// Calling the library's own blocking helpers - <c>Wait</c>, <c>WaitAll</c>, <c>TryWait</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same problem as <see cref="BlockingOnRedisCall"/> - a thread held for the round-trip, while the reply
    /// needs a thread of its own - but reached through the API the library itself offers for it, rather than
    /// through <c>.Result</c>. Shipping SER307 while the library provided a blessed way to do the same thing
    /// was only ever half a position.
    /// </para>
    /// <para>
    /// This is a rule rather than <c>[Obsolete]</c> deliberately, and the reason is about how it is turned
    /// off. <c>[Obsolete]</c> reports <c>CS0618</c>, which is shared with every obsoletion from every source,
    /// so a consumer who wants to silence *this* has to silence *all* of them - including deprecations in
    /// their own code and in unrelated packages. <c>ObsoleteAttribute.DiagnosticId</c> would solve that, but
    /// it is net5+ and this library still targets netstandard2.0, so it would give a granular ID on some
    /// target frameworks and <c>CS0618</c> on others. An ID of our own behaves the same everywhere, and sits
    /// with the rest of the family. <c>[Experimental]</c> was considered and rejected: it is granular, but
    /// these APIs are long-standing rather than preview, so it would be saying something untrue.
    /// </para>
    /// <para>
    /// Worth revisiting rather than settled: if netstandard2.0 and net4x are ever dropped, the attribute
    /// becomes strictly the better instrument - it is metadata, so it needs no analyzer to be loaded and
    /// works in every tool - and this rule could retire in its favour. A hybrid was considered now and is
    /// not worth it: the attribute would need <c>#if</c> on the interface *and* on ConnectionMultiplexer's
    /// own members (marking only the interface does not warn through the class), and on modern targets both
    /// mechanisms would report at the same location.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BlockingHelper = new(
        id: "SER308",
        title: "Blocking on a task through the library's Wait helpers",
        messageFormat: "'{0}' blocks the calling thread until the task completes, and the reply needs a thread of its own to be processed; await the task instead",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The Wait/WaitAll/TryWait helpers block the calling thread while waiting for a reply whose processing also needs the thread-pool; enough of these will starve the pool.",
        helpLinkUri: "https://seredis.dev/SyncOverAsync");

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
