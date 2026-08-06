using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace StackExchange.Redis.Build;

/// <summary>
/// Spots <c>ITransaction</c>/<c>ITransactionAsync</c> usage that a single conditional command does better.
/// </summary>
/// <remarks>
/// <para>
/// Three shapes, all of them unambiguous by construction: one condition guarding one command on a
/// syntactically identical key (SER300-SER302), two commands that one compound command covers (SER303), and the
/// same command queued repeatedly where a variadic overload covers it (SER304).
/// </para>
/// <para>
/// Deliberately conservative, because this ships to every consumer of the package and a false positive on
/// correct code is worse than staying quiet. Anything cleverer - a condition on a different key, a transaction
/// whose result feeds back into control flow, commands queued in a loop, a transaction handed to another method
/// - is left alone on purpose: partial inference that works inconsistently would be more confusing than none.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransactionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(
            Diagnostics.PreferConditionalArgument,
            Diagnostics.PreferNewerAtomicOperation,
            Diagnostics.RedundantCondition,
            Diagnostics.PreferCompoundCommand,
            Diagnostics.PreferVariadicOverload);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // The cheap short-circuit that matters: this analyzer ships to everyone who references the package,
        // but the vast majority of compilations contain no transactions at all. Resolving the types once per
        // compilation and bailing means those projects pay a couple of metadata lookups and nothing else.
        context.RegisterCompilationStartAction(static ctx =>
        {
            if (KnownSymbols.TryCreate(ctx.Compilation) is not { } known) return;

            // read once per compilation, not per block: it cannot change within one
            var declaredMinVersion = ServerVersion.FromOptions(ctx.Options);
            ctx.RegisterOperationBlockAction(blockCtx => Analyze(blockCtx, known, declaredMinVersion));
        });
    }

    private sealed class KnownSymbols
    {
        private KnownSymbols(INamedTypeSymbol condition, INamedTypeSymbol? transaction, INamedTypeSymbol? transactionAsync, INamedTypeSymbol? commandFlags)
        {
            Condition = condition;
            Transaction = transaction;
            TransactionAsync = transactionAsync;
            CommandFlags = commandFlags;
        }

        public INamedTypeSymbol Condition { get; }
        public INamedTypeSymbol? Transaction { get; }
        public INamedTypeSymbol? TransactionAsync { get; }

        /// <summary>
        /// <c>CommandFlags</c>, which every command takes and no suggestion mentions.
        /// </summary>
        /// <remarks>
        /// Singled out because the argument audit below treats an argument the suggestion does not carry as a
        /// reason to stay quiet, and flags would otherwise silence every rule for anyone who passes them. They
        /// are carried over verbatim instead, which is what the help pages say to do.
        /// </remarks>
        public INamedTypeSymbol? CommandFlags { get; }

        public static KnownSymbols? TryCreate(Compilation compilation)
        {
            // no Condition type => not our library, or a version without it; either way there is nothing here
            if (compilation.GetTypeByMetadataName("StackExchange.Redis.Condition") is not { } condition) return null;

            var transaction = compilation.GetTypeByMetadataName("StackExchange.Redis.ITransaction");
            var transactionAsync = compilation.GetTypeByMetadataName("StackExchange.Redis.ITransactionAsync");
            if (transaction is null && transactionAsync is null) return null;

            return new KnownSymbols(condition, transaction, transactionAsync, compilation.GetTypeByMetadataName("StackExchange.Redis.CommandFlags"));
        }

        public bool IsCommandFlags(ITypeSymbol? type)
            => CommandFlags is not null && SymbolEqualityComparer.Default.Equals(type, CommandFlags);

        public bool IsTransaction(ITypeSymbol? type)
            => type is not null
               && ((Transaction is not null && SymbolEqualityComparer.Default.Equals(type, Transaction))
                   || (TransactionAsync is not null && SymbolEqualityComparer.Default.Equals(type, TransactionAsync)));
    }

    private static void Analyze(OperationBlockAnalysisContext context, KnownSymbols known, ServerVersion declaredMinVersion)
    {
        foreach (var block in context.OperationBlocks)
        {
            // one pass, gathering per-transaction-local usage; most blocks contain nothing and fall straight out
            Dictionary<ISymbol, Usage>? usages = null;

            foreach (var operation in block.Descendants())
            {
                // the transaction is identified by the local it was assigned to; anything else (a field, a
                // fluent chain) is out of scope by design. Walking local *references* rather than invocations
                // is what lets us see the uses that are not calls at all - see the escape check below.
                if (operation is not ILocalReferenceOperation { Local: { } local }) continue;
                context.CancellationToken.ThrowIfCancellationRequested();

                if (!known.IsTransaction(local.Type)) continue;

                usages ??= new Dictionary<ISymbol, Usage>(SymbolEqualityComparer.Default);
                if (!usages.TryGetValue(local, out var usage)) usages[local] = usage = new Usage();

                if (operation.Parent is IInvocationOperation invocation
                    && invocation.Instance is ILocalReferenceOperation { Local: { } instanceLocal }
                    && SymbolEqualityComparer.Default.Equals(instanceLocal, local))
                {
                    // tran.Something(...) - a queued command, a condition, or the terminator
                    var repeats = !TryGetBranch(invocation, block, local, out var branch);
                    usage.Add(invocation, known, branch, repeats);
                }
                else
                {
                    // The transaction is used as a value: passed to a helper, stored, captured, returned. We
                    // cannot see what that other code queues, so our counts are no longer the whole story and
                    // any suggestion would be based on a partial view. Give up on this local entirely.
                    usage.Disqualify();
                }
            }

            if (usages is null) continue;

            // Locals that are written somewhere in this block, which is what makes comparing key expressions by
            // text unsound: "key" and "key" are the same text but not the same key if it was reassigned in
            // between. Declarations do not count - only later writes - so the common case stays clean.
            //
            // Deliberately a second walk, and deliberately after the bail-out above rather than before it: this
            // analyzer ships to everyone who references the package, where the overwhelming majority of blocks
            // hold no transaction at all. Those blocks now pay one walk instead of two, and this one runs only
            // for the handful that have something to say.
            HashSet<ISymbol>? reassignedLocals = null;
            foreach (var operation in block.Descendants())
            {
                if (LocalWrittenBy(operation) is { } written)
                {
                    reassignedLocals ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                    reassignedLocals.Add(written);
                }
            }

            foreach (var pair in usages)
            {
                if (pair.Value.TryGetSuggestion(reassignedLocals) is not { } found) continue;

                // The suggestion is only actionable on a server that has the command, and we cannot see the
                // server - so if the project has told us its floor, respect it. Unset shows everything.
                if (!found.MinVersion.IsSatisfiedBy(declaredMinVersion)) continue;

                var location = pair.Value.LocationFor(found.Rule);
                context.ReportDiagnostic(found.Rule switch
                {
                    Rule.NewerAtomicOperation => Diagnostic.Create(
                        Diagnostics.PreferNewerAtomicOperation,
                        location,
                        found.First,
                        found.Second,
                        found.Suggestion,
                        found.MinVersion.ToString()),

                    Rule.RedundantCondition => Diagnostic.Create(
                        Diagnostics.RedundantCondition,
                        location,
                        found.First,
                        found.Second,
                        found.Suggestion),

                    // family D's versions vary from "any" (SMOVE) to 8.0 (HGETDEL), so the clause is built
                    // rather than baked into the format - see Diagnostics.PreferCompoundCommand
                    Rule.VariadicOverload => Diagnostic.Create(
                        Diagnostics.PreferVariadicOverload,
                        location,
                        found.First,
                        found.Second,
                        found.Suggestion,
                        VersionClause(found.MinVersion)),

                    Rule.CompoundCommand => Diagnostic.Create(
                        Diagnostics.PreferCompoundCommand,
                        location,
                        found.First,
                        found.Second,
                        found.Suggestion,
                        VersionClause(found.MinVersion)),

                    _ => Diagnostic.Create(
                        Diagnostics.PreferConditionalArgument,
                        location,
                        found.First,
                        found.Second,
                        found.Suggestion),
                });
            }
        }
    }

    /// <summary>
    /// The local this operation writes to, if it writes to one.
    /// </summary>
    /// <remarks>
    /// A variable *declaration* is not a write for this purpose - the interesting case is a local that held one
    /// key when a command was queued and a different one by the time the next was, which only a later assignment
    /// can produce. <c>ref</c>/<c>out</c> arguments count, because the callee may do exactly that.
    /// </remarks>
    private static ISymbol? LocalWrittenBy(IOperation operation) => operation switch
    {
        ISimpleAssignmentOperation { Target: ILocalReferenceOperation { Local: { } local } } => local,
        ICompoundAssignmentOperation { Target: ILocalReferenceOperation { Local: { } local } } => local,
        IIncrementOrDecrementOperation { Target: ILocalReferenceOperation { Local: { } local } } => local,
        IArgumentOperation
        {
            Parameter.RefKind: RefKind.Ref or RefKind.Out,
            Value: ILocalReferenceOperation { Local: { } local },
        } => local,
        _ => null,
    };

    /// <summary>
    /// The trailing " (requires server x.y or later)", or nothing where the suggestion needs no particular one.
    /// </summary>
    private static string VersionClause(ServerVersion version)
        => version.IsSpecified ? " (requires server " + version + " or later)" : "";

    /// <summary>
    /// Where a call sits within the block: which branch of it, and whether it can run more than once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counting call sites is a syntactic approximation, and this is where it breaks. Two ways, needing two
    /// different answers. A call that can run <em>repeatedly</em> - inside a loop, or inside a lambda or local
    /// function whose invocation count we cannot see at all - is one call site and N queued commands, so it is
    /// not collapsible into anything: <c>false</c>, and the caller disqualifies the whole transaction.
    /// </para>
    /// <para>
    /// That repeat risk belongs to the <em>captured</em> transaction, not to the function boundary itself. A
    /// transaction that is a local of the lambda or local function we are walking out of is created afresh on
    /// each invocation, so one invocation holds one entire transaction and the counts within it are exact -
    /// stop at that boundary and report what we have. Whatever encloses the function then governs how many
    /// transactions there are, not what goes into each. Getting this wrong silences the analyzer completely
    /// for anyone who writes their transaction in a local function, top-level statements included, since there
    /// the whole program body is one synthesised method and Roslyn hands us no separate block for the nested
    /// function.
    /// </para>
    /// <para>
    /// A call under an <c>if</c>, <c>switch</c> or <c>try</c> is different: it runs at most once, so it is fine
    /// on its own terms, but only if every <em>other</em> call on the same transaction is under the same one.
    /// Two commands in the same <c>if</c> body always queue together and a compound command really does replace
    /// them; the same two in opposite arms of an <c>if</c>/<c>else</c> never queue together at all, and
    /// "collapsing" them would queue a command the code deliberately did not. Hence the innermost enclosing
    /// <em>branch</em> rather than a plain "is it conditional" flag - and the branch, not the branching
    /// operation, or the two arms of one <c>if</c> would compare equal.
    /// </para>
    /// </remarks>
    private static bool TryGetBranch(IOperation operation, IOperation block, ISymbol transaction, out SyntaxNode? branch)
    {
        branch = null;
        var previous = operation;
        for (var node = operation.Parent; node is not null && node != block; node = node.Parent)
        {
            switch (node)
            {
                case ILoopOperation:
                    return false;

                // captured from outside: unbounded, so no. Declared inside: this function body is effectively
                // the block, and we are done walking
                case IAnonymousFunctionOperation { Symbol: { } lambda }:
                    return DeclaredIn(lambda);
                case ILocalFunctionOperation { Symbol: { } localFunction }:
                    return DeclaredIn(localFunction);

                // keep walking after finding one: an enclosing loop still trumps it
                case IConditionalOperation:
                case ISwitchOperation:
                case ISwitchExpressionOperation:
                case ITryOperation:
                    branch ??= previous.Syntax;
                    break;
            }

            previous = node;
        }

        return true;

        bool DeclaredIn(IMethodSymbol function)
            => SymbolEqualityComparer.Default.Equals(transaction.ContainingSymbol, function);
    }

    /// <summary>
    /// Which kind of rewrite this is, and so which diagnostic ID reports it.
    /// </summary>
    /// <remarks>
    /// Kept distinct from <see cref="Rewrite.MinVersion"/> on purpose. The ID is about the *kind* of fix, which
    /// is what a consumer configures severity on and what they read a doc page about; the version is data about
    /// one mapping and moves as servers ship.
    /// </remarks>
    private enum Rule
    {
        /// <summary>SER300 - the command already takes this condition as an argument.</summary>
        ConditionalArgument,

        /// <summary>SER301 - a newer single command subsumes the condition and the write.</summary>
        NewerAtomicOperation,

        /// <summary>SER302 - the condition tells the caller nothing the write does not already report.</summary>
        RedundantCondition,

        /// <summary>SER303 - no condition at all; two queued operations that are one command.</summary>
        CompoundCommand,

        /// <summary>SER304 - the same command queued repeatedly, where one variadic call does the lot.</summary>
        VariadicOverload,
    }

    /// <summary>
    /// A rewrite we are prepared to suggest, and what it needs.
    /// </summary>
    private readonly struct Rewrite
    {
        public Rewrite(Rule rule, string first, string second, string suggestion, ServerVersion minVersion)
        {
            Rule = rule;
            First = first;
            Second = second;
            Suggestion = suggestion;
            MinVersion = minVersion;
        }

        public Rule Rule { get; }

        /// <summary>
        /// The condition; for <see cref="Rule.CompoundCommand"/> the first queued operation, and for
        /// <see cref="Rule.VariadicOverload"/> the operation that was repeated.
        /// </summary>
        public string First { get; }

        /// <summary>
        /// The queued operation; for <see cref="Rule.CompoundCommand"/> the second one, and for
        /// <see cref="Rule.VariadicOverload"/> how many times it was queued.
        /// </summary>
        public string Second { get; }

        /// <summary>The suggested call, as shown to the user.</summary>
        public string Suggestion { get; }

        public ServerVersion MinVersion { get; }
    }

    /// <summary>
    /// The method name as the mapping tables spell it: the sync name, since the tables describe commands rather
    /// than overloads and both surfaces map to the same suggestion.
    /// </summary>
    private static string Trim(string name)
        => name.EndsWith("Async", StringComparison.Ordinal) ? name.Substring(0, name.Length - 5) : name;

    /// <summary>
    /// One command queued on the transaction, reduced to what the mappings need to match on.
    /// </summary>
    private readonly struct QueuedOperation
    {
        public QueuedOperation(string name, string? key, string? member, List<ISymbol>? reads, List<string>? supplied)
        {
            DisplayName = name;
            Name = Trim(name);
            Key = key;
            Member = member;
            Reads = reads;
            Supplied = supplied;
        }

        /// <summary>The method name with any <c>Async</c> suffix removed, for matching against the tables.</summary>
        public string Name { get; }

        /// <summary>
        /// The method name as written, for the message.
        /// </summary>
        /// <remarks>
        /// The suffix matters here even though it does not for matching: the reader is looking for this call in
        /// their own code, so naming <c>StringSetAsync</c> when that is what they wrote saves them a beat.
        /// </remarks>
        public string DisplayName { get; }

        /// <summary>Source text of the first argument - the key, for every command we map.</summary>
        public string? Key { get; }

        /// <summary>Source text of the second argument: a hash field, or a set member, where there is one.</summary>
        public string? Member { get; }

        /// <summary>
        /// Locals read by the key/member expressions, so we can tell whether comparing them by text is sound.
        /// </summary>
        public List<ISymbol>? Reads { get; }

        /// <summary>
        /// Parameter names the caller actually wrote an argument for, other than <c>CommandFlags</c>.
        /// </summary>
        /// <remarks>
        /// Every mapping says which of these its suggestion still carries; anything else the caller wrote would
        /// be silently dropped by the rewrite, so it declines instead. Omitted optional arguments are not
        /// listed - they carry no intent and are what the suggested form would default to anyway.
        /// </remarks>
        public List<string>? Supplied { get; }
    }

    /// <summary>
    /// What we saw done with one transaction local.
    /// </summary>
    private sealed class Usage
    {
        /// <summary>
        /// Beyond this many queued commands, stop recording and stay quiet.
        /// </summary>
        /// <remarks>
        /// A backstop rather than a meaningful limit: the variadic shape is unbounded in principle, so this
        /// exists only to keep one pathological method from holding an arbitrarily long list. Exceeding it costs
        /// a missed suggestion, never a wrong one, and 32 queued commands in a single hand-written transaction
        /// is already well past what this rule is for.
        /// </remarks>
        private const int MaxInterestingOperations = 32;

        private readonly List<QueuedOperation> _operations = new();
        private int _conditionCount;
        private string? _conditionFactory, _conditionKey, _conditionMember;
        private List<ISymbol>? _conditionReads;
        private bool _disqualified;
        private Location? _condition, _firstOperation;

        /// <summary>
        /// The branch every call on this transaction so far was in; <c>null</c> means the block itself.
        /// </summary>
        /// <remarks>
        /// Only meaningful once <see cref="_branchKnown"/> is set, because <c>null</c> is a real value here -
        /// "not inside any branch" is the common case and has to compare equal to itself.
        /// </remarks>
        private SyntaxNode? _branch;
        private bool _branchKnown;

        /// <summary>
        /// Where to report, which depends on the rule: the condition is the thing to remove for most of them,
        /// but family D has no condition at all, so its report goes on the first queued command.
        /// </summary>
        public Location? LocationFor(Rule rule)
            => rule is Rule.CompoundCommand or Rule.VariadicOverload ? _firstOperation : _condition;

        /// <summary>
        /// Something about this usage puts it beyond what we can reason about; stay silent regardless of counts.
        /// </summary>
        public void Disqualify() => _disqualified = true;

        public void Add(IInvocationOperation invocation, KnownSymbols known, SyntaxNode? branch, bool repeats)
        {
            if (repeats)
            {
                Disqualify();
                return;
            }

            // The terminator is Execute/ExecuteAsync *as declared on the transaction interface*. Matching the
            // name alone also swallowed IDatabaseAsync.ExecuteAsync(string command, params object[] args) -
            // which is a queued command, and the one people reach for precisely when the library has no
            // wrapper for what they want. A queued command we cannot see makes every count below a lie: the
            // pair rules would collapse two operations that had a third between them.
            if (invocation.TargetMethod.Name is "Execute" or "ExecuteAsync"
                && known.IsTransaction(invocation.TargetMethod.ContainingType))
            {
                return;
            }

            // Deliberately after the terminator check and not before it: the terminator is allowed to sit
            // somewhere else entirely - "queue it all, then commit it inside an if" is ordinary code, and says
            // nothing about whether the queued commands belong together.
            if (_branchKnown && _branch != branch)
            {
                Disqualify();
                return;
            }

            _branch = branch;
            _branchKnown = true;

            switch (invocation.TargetMethod.Name)
            {
                case "AddCondition":
                    _conditionCount++;
                    _condition ??= invocation.Syntax.GetLocation();

                    // the argument is expected to be a Condition.Xxx(...) factory call; if it is anything else
                    // (a variable, a helper method) we cannot know what it tests, so leave the names null and
                    // the mapping below will decline
                    if (invocation.Arguments.Length == 1
                        && Unwrap(invocation.Arguments[0].Value) is IInvocationOperation factory
                        && SymbolEqualityComparer.Default.Equals(factory.TargetMethod.ContainingType, known.Condition))
                    {
                        _conditionFactory = factory.TargetMethod.Name;
                        _conditionKey = ArgumentText(factory, 0);
                        _conditionMember = ArgumentText(factory, 1);
                        _conditionReads = LocalsRead(factory);
                    }

                    break;

                default:
                    // everything else queued on the transaction is a redis operation - including a raw
                    // ExecuteAsync("SOMECMD", ...), which maps to nothing and so can only ever suppress
                    _firstOperation ??= invocation.Syntax.GetLocation();
                    if (_operations.Count < MaxInterestingOperations)
                    {
                        _operations.Add(new QueuedOperation(
                            invocation.TargetMethod.Name,
                            ArgumentText(invocation, 0),
                            ArgumentText(invocation, 1),
                            LocalsRead(invocation),
                            SuppliedArguments(invocation, known)));
                    }
                    else
                    {
                        Disqualify();
                    }

                    break;
            }
        }

        public Rewrite? TryGetSuggestion(HashSet<ISymbol>? reassignedLocals)
        {
            if (_disqualified) return null;

            // Every shape below decides by comparing key/member expressions as text. That is only sound while
            // the locals involved hold the same value throughout: if one was reassigned between the two calls,
            // identical text means two different keys, and the suggestion would silently change behaviour.
            if (reassignedLocals is not null && ReadsAny(reassignedLocals)) return null;

            if (_conditionCount == 1 && _operations.Count == 1)
            {
                // families A, B and C: one guard over one command
                return TryGuardedOperation(_operations[0]);
            }

            if (_conditionCount != 0 || _operations.Count < 2) return null;

            // family D, two flavours. A pair of *different* commands that one compound command covers, or the
            // same command repeated, which the variadic overload covers. They cannot both match, because one
            // wants the names to differ and the other wants them identical.
            return (_operations.Count == 2 ? TryCommandPair(_operations[0], _operations[1]) : null)
                   ?? TryVariadic();
        }

        private bool ReadsAny(HashSet<ISymbol> reassignedLocals)
        {
            if (Contains(_conditionReads, reassignedLocals)) return true;
            foreach (var operation in _operations)
            {
                if (Contains(operation.Reads, reassignedLocals)) return true;
            }

            return false;

            static bool Contains(List<ISymbol>? reads, HashSet<ISymbol> reassigned)
            {
                if (reads is null) return false;
                foreach (var read in reads)
                {
                    if (reassigned.Contains(read)) return true;
                }

                return false;
            }
        }

        private Rewrite? TryGuardedOperation(QueuedOperation operation)
        {
            if (_conditionFactory is null) return null;

            // the same key expression in both; see ArgumentText for why this is syntactic
            if (_conditionKey is null || operation.Key is null || _conditionKey != operation.Key) return null;

            if (Map(_conditionFactory, operation.Name) is not { } mapped) return null;

            // Where the condition names a hash field or a set member, it has to be the *same* one the command
            // touches: a condition about member "a" says nothing about removing member "b", and collapsing the
            // two would silently drop a real guard. Only some mappings have a member at all, hence the flag.
            if (mapped.SameMember
                && (_conditionMember is null || operation.Member is null || _conditionMember != operation.Member))
            {
                return null;
            }

            if (!IsCovered(operation, mapped.Covered)) return null;

            return new Rewrite(
                mapped.Rule,
                "Condition." + _conditionFactory,
                operation.DisplayName,
                mapped.Suggestion,
                mapped.MinVersion);
        }

        private static Rewrite? TryCommandPair(QueuedOperation first, QueuedOperation second)
        {
            if (MapPair(first, second) is not { } mapped) return null;
            if (!IsCovered(first, mapped.CoveredFirst) || !IsCovered(second, mapped.CoveredSecond)) return null;
            return new Rewrite(Rule.CompoundCommand, first.DisplayName, second.DisplayName, mapped.Suggestion, mapped.MinVersion);
        }

        /// <summary>
        /// The same command queued several times over, where one variadic call does the lot.
        /// </summary>
        private Rewrite? TryVariadic()
        {
            var first = _operations[0];
            for (var i = 1; i < _operations.Count; i++)
            {
                if (_operations[i].Name != first.Name) return null;
            }

            if (MapVariadic(first.Name) is not { } mapped) return null;

            // Which keys the variadic form takes is the whole distinction here. SADD and friends take one key
            // and many values, so every call has to be on the *same* key - N calls across different keys have no
            // single-command form. MSET/MGET/DEL take many keys, so those must be different keys, which also
            // avoids arguing about what a repeated key would mean.
            for (var i = 0; i < _operations.Count; i++)
            {
                if (_operations[i].Key is null) return null;
                if (!IsCovered(_operations[i], mapped.Covered)) return null;
                if (mapped.ManyKeys)
                {
                    if (mapped.RequiresMember && _operations[i].Member is null) return null;
                    for (var j = i + 1; j < _operations.Count; j++)
                    {
                        if (_operations[i].Key == _operations[j].Key) return null;
                    }
                }
                else
                {
                    if (_operations[i].Key != first.Key) return null;
                    if (mapped.RequiresMember && _operations[i].Member is null) return null;
                }
            }

            return new Rewrite(
                Rule.VariadicOverload,
                first.DisplayName,
                _operations.Count.ToString(CultureInfo.InvariantCulture),
                mapped.Suggestion,
                mapped.MinVersion);
        }

        /// <summary>
        /// Commands with a variadic overload that subsumes N separate calls.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Versions are <see cref="ServerVersion.Any"/> for all but one. The variadic forms are old - 2.4 for the
        /// one-key-many-values group, 3.0.3 for multi-key EXISTS, 1.0 for MSET/MGET/DEL - and all of it predates
        /// anything realistically in service, so naming a version would be noise rather than information.
        /// SMISMEMBER is the exception at 6.2, recent enough that somebody might actually be below it.
        /// </para>
        /// <para>
        /// Deliberately absent: N x <c>ListLeftPop</c> across keys is *not* LMPOP. LMPOP pops from the first
        /// non-empty key of those given, not from each of them, so it is a different operation however similar
        /// the argument lists look. Same for ZMPOP.
        /// </para>
        /// </remarks>
        private static (string Suggestion, bool ManyKeys, bool RequiresMember, ServerVersion MinVersion, string Covered)? MapVariadic(string operation)
            => operation switch
            {
                // one key, many values
                "SetAdd" => ("SetAdd[Async](key, values)", false, true, ServerVersion.Any, "key,value"),
                "SetRemove" => ("SetRemove[Async](key, values)", false, true, ServerVersion.Any, "key,value"),
                "SortedSetAdd" => ("SortedSetAdd[Async](key, entries)", false, true, ServerVersion.Any, "key,member,score"),
                "SortedSetRemove" => ("SortedSetRemove[Async](key, members)", false, true, ServerVersion.Any, "key,member"),
                "HashSet" => ("HashSet[Async](key, entries)", false, true, ServerVersion.Any, "key,hashField,value"),
                "HashDelete" => ("HashDelete[Async](key, fields)", false, true, ServerVersion.Any, "key,hashField"),
                "ListLeftPush" => ("ListLeftPush[Async](key, values)", false, true, ServerVersion.Any, "key,value"),
                "ListRightPush" => ("ListRightPush[Async](key, values)", false, true, ServerVersion.Any, "key,value"),

                // SMISMEMBER, which unlike the rest of these is recent; it has no RedisFeatures gate to cite
                "SetContains" => ("SetContains[Async](key, values), which returns a bool per value", false, true, new ServerVersion(6, 2), "key,value"),

                // many keys
                "KeyDelete" => ("KeyDelete[Async](keys)", true, false, ServerVersion.Any, "key"),
                "KeyExists" => ("KeyExists[Async](keys), which returns how many exist", true, false, ServerVersion.Any, "key"),
                "StringGet" => ("StringGet[Async](keys)", true, false, ServerVersion.Any, "key"),
                // MSET takes one expiry and one when for the whole batch, not one per key; the variadic
                // overload's own expiry:/when: cannot express what N separate calls each said, so Covered
                // stops at the pair that MSET does carry
                "StringSet" => ("StringSet[Async](KeyValuePair<RedisKey, RedisValue>[])", true, false, ServerVersion.Any, "key,value"),

                _ => null,
            };

        /// <summary>
        /// The condition/operation pairs that have an exact single-command equivalent.
        /// </summary>
        /// <remarks>
        /// The version is the server the *suggestion* needs, not the one the flagged code needs. Family A is
        /// <see cref="ServerVersion.Any"/> because the conditional argument has existed as long as the command
        /// (and where it has not quite - ZADD NX arrived in 3.0.2 - it predates the oldest server this library
        /// supports, so saying so would be noise).
        /// </remarks>
        private static (Rule Rule, string Suggestion, ServerVersion MinVersion, bool SameMember, string? Covered)? Map(string condition, string operation)
            => (condition, operation) switch
            {
                // -- family A: the command already takes this condition as an argument; any server version --
                // Covered omits "when": these suggestions *are* a when: argument, so a caller who wrote their
                // own has said something we would be overwriting rather than moving.
                ("KeyNotExists", "StringSet") => (Rule.ConditionalArgument, "StringSet[Async](key, value, When.NotExists)", ServerVersion.Any, false, "key,value,expiry,keepTtl"),
                ("KeyExists", "StringSet") => (Rule.ConditionalArgument, "StringSet[Async](key, value, When.Exists)", ServerVersion.Any, false, "key,value,expiry,keepTtl"),
                ("HashNotExists", "HashSet") => (Rule.ConditionalArgument, "HashSet[Async](key, field, value, When.NotExists)", ServerVersion.Any, true, "key,hashField,value"),
                // SortedSetWhen, not When: the When overload is [EditorBrowsable(Never)] and the SortedSetWhen
                // one is the canonical spelling, so suggesting When would push callers at a hidden overload
                ("SortedSetNotContains", "SortedSetAdd") => (Rule.ConditionalArgument, "SortedSetAdd[Async](key, member, score, SortedSetWhen.NotExists)", ServerVersion.Any, true, "key,member,score"),
                ("SortedSetContains", "SortedSetAdd") => (Rule.ConditionalArgument, "SortedSetAdd[Async](key, member, score, SortedSetWhen.Exists)", ServerVersion.Any, true, "key,member,score"),
                ("KeyNotExists", "KeyRename") => (Rule.ConditionalArgument, "KeyRename[Async](key, newKey, When.NotExists)", ServerVersion.Any, false, "key,newKey"),

                // -- family B: a newer single command subsumes condition and write --
                // 8.4: SET IFEQ/IFNE and DELIFEQ; see RedisFeatures.SetWithValueCheck / DeleteWithValueCheck
                ("StringEqual", "StringSet") => (Rule.NewerAtomicOperation, "StringSet[Async](key, value, ValueCondition.Equal(expected))", new ServerVersion(8, 4), false, "key,value,expiry"),
                ("StringNotEqual", "StringSet") => (Rule.NewerAtomicOperation, "StringSet[Async](key, value, ValueCondition.NotEqual(expected))", new ServerVersion(8, 4), false, "key,value,expiry"),
                ("StringEqual", "KeyDelete") => (Rule.NewerAtomicOperation, "StringDelete[Async](key, ValueCondition.Equal(expected)), or LockRelease[Async]", new ServerVersion(8, 4), false, "key"),
                ("StringNotEqual", "KeyDelete") => (Rule.NewerAtomicOperation, "StringDelete[Async](key, ValueCondition.NotEqual(expected))", new ServerVersion(8, 4), false, "key"),

                // -- family C: the write already reports what the condition was checking --
                // These have always worked this way, so no version applies. The fix deletes the transaction
                // rather than moving an argument, and what the caller observes changes: Execute() returning
                // false ("the guard failed") becomes the command itself returning false ("I did nothing").
                // Covered is null throughout: the command is kept exactly as written, so there is no argument
                // the rewrite could drop, however exotic.
                ("SetNotContains", "SetAdd") => (Rule.RedundantCondition, "SetAdd[Async](key, value), which returns false if the member was already there", ServerVersion.Any, true, null),
                ("SetContains", "SetRemove") => (Rule.RedundantCondition, "SetRemove[Async](key, value), which returns false if the member was not there", ServerVersion.Any, true, null),
                ("SortedSetContains", "SortedSetRemove") => (Rule.RedundantCondition, "SortedSetRemove[Async](key, member), which returns false if the member was not there", ServerVersion.Any, true, null),
                ("HashExists", "HashDelete") => (Rule.RedundantCondition, "HashDelete[Async](key, field), which returns false if the field was not there", ServerVersion.Any, true, null),
                ("KeyExists", "KeyDelete") => (Rule.RedundantCondition, "KeyDelete[Async](key), which returns false if the key did not exist", ServerVersion.Any, false, null),
                ("KeyExists", "KeyExpire") => (Rule.RedundantCondition, "KeyExpire[Async](key, expiry), which returns false if the key did not exist", ServerVersion.Any, false, null),

                // Deliberately absent from family C: ListIndexExists + ListSetByIndex. LSET reports an
                // out-of-range index by failing, not by returning false (ListSetByIndex returns Task, not
                // Task<bool>), so dropping the condition turns an aborted transaction into an exception -
                // a change of behaviour, not a simplification.

                // Deliberately absent, because no atomic equivalent exists and suggesting one would be wrong:
                //   HashExists + HashSet      - there is no HSETXX; the nearest thing is a different method
                //                               (HashFieldSet with ValueCondition.Exists, HSETEX FXX, 8.0+)
                //   HashEqual/HashNotEqual    - no server-side hash compare-and-set at all
                //   ListIndexEqual + ListSet  - likewise
                //   *Length* conditions       - likewise
                _ => null,
            };

        /// <summary>
        /// Family D: two queued commands, no condition, that are one compound command between them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Order matters here in a way it did not for the guarded families, because these commands return a
        /// value: <c>SET ... GET</c> hands back the value from *before* the write, so it matches a queued get
        /// followed by a set, and not the other way round.
        /// </para>
        /// <para>
        /// A read whose result feeds the write is impossible to express here at all - inside a transaction the
        /// read's result is an unresolved <c>Task</c>, so the caller cannot use it. That rules out the pairing
        /// that looks most tempting, <c>ListRightPop</c> + <c>ListLeftPush</c> = <c>LMOVE</c>: whatever value is
        /// being pushed, it is not the one that was popped, so LMOVE would not do the same thing. SMOVE below is
        /// fine by contrast, because the member is a value the caller already has and passes to both calls.
        /// </para>
        /// </remarks>
        private static (string Suggestion, ServerVersion MinVersion, string CoveredFirst, string CoveredSecond)? MapPair(QueuedOperation first, QueuedOperation second)
        {
            // 6.2: GETDEL / GETEX / SET ... GET; see RedisFeatures.GetDelete and SetAndGet
            var v6_2 = new ServerVersion(6, 2);

            if (SameKey(first, second))
            {
                switch (first.Name, second.Name)
                {
                    case ("StringGet", "KeyDelete"):
                        return ("StringGetDelete[Async](key)", v6_2, "key", "key");

                    // GETEX has no NX/XX, so KeyExpire's ExpireWhen is not covered and a caller who wrote
                    // one keeps their transaction
                    case ("StringGet", "KeyExpire"):
                        return ("StringGetSetExpiry[Async](key, expiry)", v6_2, "key", "key,expiry");
                    case ("StringGet", "KeyPersist"):
                        return ("StringGetSetExpiry[Async](key, null)", v6_2, "key", "key");
                    case ("StringGet", "StringSet"):
                        return ("StringSetAndGet[Async](key, value)", v6_2, "key", "key,value,expiry,keepTtl,when");

                    // SET ... EX, which is why this one needs no particular server: setting a value and its
                    // lifetime in one command is as old as SET's options (2.6.12). The order is load-bearing
                    // in the other direction to the reads above - SET *clears* any TTL, so an EXPIRE followed
                    // by a SET leaves no expiry at all and is emphatically not this.
                    //
                    // "expiry" is absent from the first coverage set on purpose: a StringSet that already
                    // carries one, followed by an EXPIRE that overrides it, is not one command with one
                    // lifetime and we should not be guessing which of the two the caller meant.
                    case ("StringSet", "KeyExpire"):
                        return ("StringSet[Async](key, value, expiry)", ServerVersion.Any, "key,value,when", "key,expiry");

                    // HGETDEL is 8.0; it has no RedisFeatures gate to point at
                    case ("HashGet", "HashDelete") when SameMember(first, second):
                        return ("HashFieldGetAndDelete[Async](key, field)", new ServerVersion(8, 0), "key,hashField", "key,hashField");
                }

                return null;
            }

            // SMOVE, which is as old as sets themselves. Two different keys by definition - and the same member
            // in both calls, or it is not one move. Either order queues the same pair of effects.
            if (SameMember(first, second)
                && ((first.Name == "SetRemove" && second.Name == "SetAdd")
                    || (first.Name == "SetAdd" && second.Name == "SetRemove")))
            {
                return ("SetMove[Async](source, destination, value)", ServerVersion.Any, "key,value", "key,value");
            }

            return null;

            static bool SameKey(QueuedOperation a, QueuedOperation b)
                => a.Key is not null && b.Key is not null && a.Key == b.Key;

            static bool SameMember(QueuedOperation a, QueuedOperation b)
                => a.Member is not null && b.Member is not null && a.Member == b.Member;
        }

        /// <summary>
        /// The source text of an argument, used as a cheap "same key?" / "same member?" test.
        /// </summary>
        /// <remarks>
        /// Deliberately syntactic. Comparing keys semantically is not possible in general (they are values,
        /// not symbols), so requiring the *same expression text* keeps false positives near zero at the cost
        /// of missing cases where the same key is spelled two different ways. That trade is the right way
        /// round for a shipped analyzer.
        /// </remarks>
        private static List<ISymbol>? LocalsRead(IInvocationOperation invocation)
        {
            List<ISymbol>? locals = null;
            for (var i = 0; i < 2 && i < invocation.Arguments.Length; i++)
            {
                foreach (var node in invocation.Arguments[i].Value.DescendantsAndSelf())
                {
                    if (node is ILocalReferenceOperation { Local: { } local })
                    {
                        (locals ??= new List<ISymbol>()).Add(local);
                    }
                }
            }

            return locals;
        }

        /// <summary>
        /// Does the suggestion still carry everything the caller wrote?
        /// </summary>
        /// <remarks>
        /// The suggestions are sketches, so this is not about spelling every argument back out - it is about
        /// arguments the suggested command cannot express at all, which the rewrite would therefore drop in
        /// silence. N x <c>StringSet(key, value, expiry)</c> is not MSET: taking that advice makes the keys
        /// permanent. Declining costs a suggestion, which is the cheap direction, and the help pages list the
        /// shapes it gives up on.
        /// </remarks>
        private static bool IsCovered(QueuedOperation operation, string? covered)
        {
            // null covers everything: family C keeps the command exactly as written and only drops the
            // condition, so no argument of it can go missing
            if (covered is null || operation.Supplied is not { } supplied) return true;

            foreach (var name in supplied)
            {
                if (!Covers(covered, name)) return false;
            }

            return true;

            static bool Covers(string covered, string name)
            {
                foreach (var candidate in covered.Split(','))
                {
                    if (candidate == name) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// The parameter names the caller actually wrote an argument for, other than <c>CommandFlags</c>.
        /// </summary>
        private static List<string>? SuppliedArguments(IInvocationOperation invocation, KnownSymbols known)
        {
            List<string>? names = null;
            foreach (var argument in invocation.Arguments)
            {
                if (argument.ArgumentKind != ArgumentKind.Explicit) continue;
                if (argument.Parameter is not { } parameter) continue;

                // Flags never bear on any of this: they are on every command, no suggestion mentions them, and
                // the rewrite carries them over verbatim. Recognised by name as well as by type, because the
                // consequence of failing to recognise them is not a missed exclusion but silence everywhere -
                // every command takes flags, so one unrecognised spelling would suppress every rule for anyone
                // who passes them. Belt and braces is cheap here; the type lookup is the one that can be null.
                if (parameter.Name == "flags" || known.IsCommandFlags(parameter.Type)) continue;

                (names ??= new List<string>()).Add(parameter.Name);
            }

            return names;
        }

        private static string? ArgumentText(IInvocationOperation invocation, int index)
        {
            if (invocation.Arguments.Length <= index) return null;

            // An omitted optional argument reports the *invocation* as its syntax, so two of them from one
            // call site compare equal to each other and to nothing the caller wrote. Only text somebody
            // actually typed is a key or a member.
            var argument = invocation.Arguments[index];
            return argument.ArgumentKind == ArgumentKind.Explicit ? argument.Value.Syntax.ToString() : null;
        }

        private static IOperation Unwrap(IOperation operation)
        {
            // implicit RedisKey/RedisValue conversions wrap almost every argument in this API
            while (operation is IConversionOperation { Operand: { } inner }) operation = inner;
            return operation;
        }
    }
}
