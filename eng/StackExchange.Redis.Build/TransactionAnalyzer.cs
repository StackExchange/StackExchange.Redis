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
            ctx.RegisterOperationBlockAction(blockCtx => Analyze(blockCtx, known));
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

    private static void Analyze(OperationBlockAnalysisContext context, KnownSymbols known)
    {
        foreach (var block in context.OperationBlocks)
        {
            // one pass, gathering per-transaction-local usage; most blocks contain nothing and fall straight out
            Dictionary<ISymbol, Usage>? usages = null;

            foreach (var operation in block.Descendants())
            {
                if (operation is not IInvocationOperation invocation) continue;
                context.CancellationToken.ThrowIfCancellationRequested();

                // the transaction is identified by the local it was assigned to; anything else (a field, a
                // fluent chain, a transaction passed between methods) is out of scope by design
                if (invocation.Instance is not ILocalReferenceOperation { Local: { } local }) continue;
                if (!known.IsTransaction(local.Type)) continue;

                usages ??= new Dictionary<ISymbol, Usage>(SymbolEqualityComparer.Default);
                if (!usages.TryGetValue(local, out var usage)) usages[local] = usage = new Usage();
                usage.Add(invocation, known);
            }

            if (usages is null) continue;

            foreach (var pair in usages)
            {
                if (pair.Value.TryGetSuggestion(out var conditionName, out var operationName, out var suggestion, out var needsNewerServer))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        needsNewerServer ? Diagnostics.PreferNewerAtomicOperation : Diagnostics.PreferConditionalArgument,
                        pair.Value.ReportAt,
                        conditionName,
                        operationName,
                        suggestion));
                }
            }
        }
    }

    /// <summary>
    /// What we saw done with one transaction local.
    /// </summary>
    private sealed class Usage
    {
        private int _conditionCount, _operationCount;
        private string? _conditionFactory, _conditionKey;
        private string? _operationName, _operationKey;

        public Location? ReportAt { get; private set; }

        public void Add(IInvocationOperation invocation, KnownSymbols known)
        {
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

        public bool TryGetSuggestion(out string conditionName, out string operationName, out string suggestion, out bool needsNewerServer)
        {
            conditionName = operationName = suggestion = "";
            needsNewerServer = false;

            // only the unambiguous shape: one guard, one operation, and the same key in both
            if (_conditionCount != 1 || _operationCount != 1) return false;
            if (_conditionFactory is null || _operationName is null) return false;
            if (_conditionKey is null || _operationKey is null || _conditionKey != _operationKey) return false;

            if (Map(_conditionFactory, _operationName) is not { } mapped) return false;

            conditionName = "Condition." + _conditionFactory;
            operationName = _operationName;
            (suggestion, needsNewerServer) = mapped;
            return true;
        }

        /// <summary>
        /// The condition/operation pairs that have an exact single-command equivalent.
        /// </summary>
        private static (string Suggestion, bool NeedsNewerServer)? Map(string condition, string operation)
        {
            var op = Trim(operation);
            return (condition, op) switch
            {
                // -- family A: the command already takes this condition as an argument; any server version --
                ("KeyNotExists", "StringSet") => ("StringSet(key, value, When.NotExists)", false),
                ("KeyExists", "StringSet") => ("StringSet(key, value, When.Exists)", false),
                ("HashNotExists", "HashSet") => ("HashSet(key, field, value, When.NotExists)", false),
                ("SortedSetNotContains", "SortedSetAdd") => ("SortedSetAdd(key, member, score, When.NotExists)", false),
                ("SortedSetContains", "SortedSetAdd") => ("SortedSetAdd(key, member, score, When.Exists)", false),
                ("KeyNotExists", "KeyRename") => ("KeyRename(key, newKey, When.NotExists)", false),

                // -- family B: a newer single command subsumes condition and write (compare-and-set, 8.4+) --
                ("StringEqual", "StringSet") => ("StringSet(key, value, ValueCondition.Equal(expected))", true),
                ("StringNotEqual", "StringSet") => ("StringSet(key, value, ValueCondition.NotEqual(expected))", true),
                ("StringEqual", "KeyDelete") => ("StringDelete(key, ValueCondition.Equal(expected)), or LockRelease", true),
                ("StringNotEqual", "KeyDelete") => ("StringDelete(key, ValueCondition.NotEqual(expected))", true),

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
