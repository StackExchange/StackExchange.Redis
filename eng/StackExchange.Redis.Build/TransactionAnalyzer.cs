using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace StackExchange.Redis.Build;

/// <summary>
/// Spots <c>ITransaction</c>/<c>ITransactionAsync</c> usage that a single conditional command does better.
/// </summary>
/// <remarks>
/// Deliberately conservative. It only fires on the unambiguous shape - exactly one condition guarding exactly
/// one queued operation, on a syntactically identical key - because this ships to every consumer of the
/// package, and a false positive on correct code is worse than staying quiet. Anything cleverer (several
/// operations, a condition on a different key, a transaction whose result feeds back into control flow) is
/// left alone on purpose: partial inference that works inconsistently would be more confusing than none.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransactionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(Diagnostics.PreferConditionalArgument, Diagnostics.PreferNewerAtomicOperation);

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
        private KnownSymbols(INamedTypeSymbol condition, INamedTypeSymbol? transaction, INamedTypeSymbol? transactionAsync)
        {
            Condition = condition;
            Transaction = transaction;
            TransactionAsync = transactionAsync;
        }

        public INamedTypeSymbol Condition { get; }
        public INamedTypeSymbol? Transaction { get; }
        public INamedTypeSymbol? TransactionAsync { get; }

        public static KnownSymbols? TryCreate(Compilation compilation)
        {
            // no Condition type => not our library, or a version without it; either way there is nothing here
            if (compilation.GetTypeByMetadataName("StackExchange.Redis.Condition") is not { } condition) return null;

            var transaction = compilation.GetTypeByMetadataName("StackExchange.Redis.ITransaction");
            var transactionAsync = compilation.GetTypeByMetadataName("StackExchange.Redis.ITransactionAsync");
            if (transaction is null && transactionAsync is null) return null;

            return new KnownSymbols(condition, transaction, transactionAsync);
        }

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
                    usage.Add(invocation, known, insideLoop: IsInsideLoop(invocation, block));
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

            foreach (var pair in usages)
            {
                if (pair.Value.TryGetSuggestion() is not { } found) continue;

                // The suggestion is only actionable on a server that has the command, and we cannot see the
                // server - so if the project has told us its floor, respect it. Unset shows everything.
                if (!found.MinVersion.IsSatisfiedBy(declaredMinVersion)) continue;

                context.ReportDiagnostic(found.NeedsNewerServer
                    ? Diagnostic.Create(
                        Diagnostics.PreferNewerAtomicOperation,
                        pair.Value.ReportAt,
                        found.ConditionName,
                        found.OperationName,
                        found.Suggestion,
                        found.MinVersion.ToString())
                    : Diagnostic.Create(
                        Diagnostics.PreferConditionalArgument,
                        pair.Value.ReportAt,
                        found.ConditionName,
                        found.OperationName,
                        found.Suggestion));
            }
        }
    }

    /// <summary>
    /// Is this call inside a loop, and so potentially queueing many commands from one call site?
    /// </summary>
    /// <remarks>
    /// Counting call sites is a syntactic approximation, and a loop is where it breaks: one
    /// <c>tran.StringSetAsync(key, value)</c> in a <c>foreach</c> is one call site but N queued commands, which
    /// is emphatically not collapsible into a single command. Cheap to check and it removes the whole class.
    /// </remarks>
    private static bool IsInsideLoop(IOperation operation, IOperation block)
    {
        for (var node = operation; node is not null && node != block; node = node.Parent)
        {
            if (node is ILoopOperation) return true;
        }

        return false;
    }

    /// <summary>
    /// A rewrite we are prepared to suggest, and what it needs.
    /// </summary>
    private readonly struct Rewrite
    {
        public Rewrite(string conditionName, string operationName, string suggestion, bool needsNewerServer, ServerVersion minVersion)
        {
            ConditionName = conditionName;
            OperationName = operationName;
            Suggestion = suggestion;
            NeedsNewerServer = needsNewerServer;
            MinVersion = minVersion;
        }

        public string ConditionName { get; }
        public string OperationName { get; }

        /// <summary>The suggested call, as shown to the user.</summary>
        public string Suggestion { get; }

        /// <summary>Which rule this is: the version-dependent one, or the version-free one.</summary>
        /// <remarks>
        /// Kept distinct from <see cref="MinVersion"/> on purpose. This picks the diagnostic ID, and the ID is
        /// about the *kind* of fix (move an argument vs adopt a newer command), which is what a consumer
        /// configures severity on. The version is data about one mapping and may change as servers ship.
        /// </remarks>
        public bool NeedsNewerServer { get; }

        public ServerVersion MinVersion { get; }
    }

    /// <summary>
    /// What we saw done with one transaction local.
    /// </summary>
    private sealed class Usage
    {
        private int _conditionCount, _operationCount;
        private string? _conditionFactory, _conditionKey;
        private string? _operationName, _operationKey;
        private bool _disqualified;

        public Location? ReportAt { get; private set; }

        /// <summary>
        /// Something about this usage puts it beyond what we can reason about; stay silent regardless of counts.
        /// </summary>
        public void Disqualify() => _disqualified = true;

        public void Add(IInvocationOperation invocation, KnownSymbols known, bool insideLoop)
        {
            if (insideLoop)
            {
                Disqualify();
                return;
            }

            switch (invocation.TargetMethod.Name)
            {
                case "AddCondition":
                    _conditionCount++;
                    ReportAt ??= invocation.Syntax.GetLocation();

                    // the argument is expected to be a Condition.Xxx(...) factory call; if it is anything else
                    // (a variable, a helper method) we cannot know what it tests, so leave the names null and
                    // the mapping below will decline
                    if (invocation.Arguments.Length == 1
                        && Unwrap(invocation.Arguments[0].Value) is IInvocationOperation factory
                        && SymbolEqualityComparer.Default.Equals(factory.TargetMethod.ContainingType, known.Condition))
                    {
                        _conditionFactory = factory.TargetMethod.Name;
                        _conditionKey = FirstArgumentText(factory);
                    }

                    break;

                case "Execute":
                case "ExecuteAsync":
                    break; // the terminator, not a queued operation

                default:
                    // everything else queued on the transaction is a redis operation
                    _operationCount++;
                    _operationName = invocation.TargetMethod.Name;
                    _operationKey = FirstArgumentText(invocation);
                    break;
            }
        }

        public Rewrite? TryGetSuggestion()
        {
            if (_disqualified) return null;

            // only the unambiguous shape: one guard, one operation, and the same key in both
            if (_conditionCount != 1 || _operationCount != 1) return null;
            if (_conditionFactory is null || _operationName is null) return null;
            if (_conditionKey is null || _operationKey is null || _conditionKey != _operationKey) return null;

            if (Map(_conditionFactory, _operationName) is not { } mapped) return null;

            return new Rewrite(
                "Condition." + _conditionFactory,
                _operationName,
                mapped.Suggestion,
                mapped.NeedsNewerServer,
                mapped.MinVersion);
        }

        /// <summary>
        /// The condition/operation pairs that have an exact single-command equivalent.
        /// </summary>
        /// <remarks>
        /// The version is the server the *suggestion* needs, not the one the flagged code needs. Family A is
        /// <see cref="ServerVersion.Any"/> because the conditional argument has existed as long as the command
        /// (and where it has not quite - ZADD NX arrived in 3.0.2 - it predates the oldest server this library
        /// supports, so saying so would be noise).
        /// </remarks>
        private static (string Suggestion, bool NeedsNewerServer, ServerVersion MinVersion)? Map(string condition, string operation)
        {
            var op = Trim(operation);
            return (condition, op) switch
            {
                // -- family A: the command already takes this condition as an argument; any server version --
                ("KeyNotExists", "StringSet") => ("StringSet(key, value, When.NotExists)", false, ServerVersion.Any),
                ("KeyExists", "StringSet") => ("StringSet(key, value, When.Exists)", false, ServerVersion.Any),
                ("HashNotExists", "HashSet") => ("HashSet(key, field, value, When.NotExists)", false, ServerVersion.Any),
                // SortedSetWhen, not When: the When overload is [EditorBrowsable(Never)] and the SortedSetWhen
                // one is the canonical spelling, so suggesting When would push callers at a hidden overload
                ("SortedSetNotContains", "SortedSetAdd") => ("SortedSetAdd(key, member, score, SortedSetWhen.NotExists)", false, ServerVersion.Any),
                ("SortedSetContains", "SortedSetAdd") => ("SortedSetAdd(key, member, score, SortedSetWhen.Exists)", false, ServerVersion.Any),
                ("KeyNotExists", "KeyRename") => ("KeyRename(key, newKey, When.NotExists)", false, ServerVersion.Any),

                // -- family B: a newer single command subsumes condition and write --
                // 8.4: SET IFEQ/IFNE and DELIFEQ; see RedisFeatures.SetWithValueCheck / DeleteWithValueCheck
                ("StringEqual", "StringSet") => ("StringSet(key, value, ValueCondition.Equal(expected))", true, new ServerVersion(8, 4)),
                ("StringNotEqual", "StringSet") => ("StringSet(key, value, ValueCondition.NotEqual(expected))", true, new ServerVersion(8, 4)),
                ("StringEqual", "KeyDelete") => ("StringDelete(key, ValueCondition.Equal(expected)), or LockRelease", true, new ServerVersion(8, 4)),
                ("StringNotEqual", "KeyDelete") => ("StringDelete(key, ValueCondition.NotEqual(expected))", true, new ServerVersion(8, 4)),

                // Deliberately absent, because no atomic equivalent exists and suggesting one would be wrong:
                //   HashExists + HashSet      - there is no HSETXX; the nearest thing is a different method
                //                               (HashFieldSet with ValueCondition.Exists, HSETEX FXX, 8.0+)
                //   HashEqual/HashNotEqual    - no server-side hash compare-and-set at all
                //   ListIndexEqual + ListSet  - likewise
                //   *Length* conditions       - likewise
                _ => null,
            };

            static string Trim(string name)
                => name.EndsWith("Async", StringComparison.Ordinal) ? name.Substring(0, name.Length - 5) : name;
        }

        /// <summary>
        /// The source text of the first argument, used as a cheap "same key?" test.
        /// </summary>
        /// <remarks>
        /// Deliberately syntactic. Comparing keys semantically is not possible in general (they are values,
        /// not symbols), so requiring the *same expression text* keeps false positives near zero at the cost
        /// of missing cases where the same key is spelled two different ways. That trade is the right way
        /// round for a shipped analyzer.
        /// </remarks>
        private static string? FirstArgumentText(IInvocationOperation invocation)
            => invocation.Arguments.Length == 0 ? null : invocation.Arguments[0].Value.Syntax.ToString();

        private static IOperation Unwrap(IOperation operation)
        {
            // implicit RedisKey/RedisValue conversions wrap almost every argument in this API
            while (operation is IConversionOperation { Operand: { } inner }) operation = inner;
            return operation;
        }
    }
}
