using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace StackExchange.Redis.Build;

/// <summary>
/// Spots the ways of consuming the task from a redis call that do not do what they look like: waiting on a
/// queued command before it has been sent (SER305), reading a fire-and-forget result (SER306), and blocking a
/// thread rather than awaiting (SER307).
/// </summary>
/// <remarks>
/// <para>
/// SER305 is the mistake that "always await your tasks" teaches people to make, and it does not fail loudly:
/// the task simply never completes, so the caller hangs. Unlike the suggestion rules in
/// <see cref="TransactionAnalyzer"/>, it is reported as an error - see
/// <see cref="Diagnostics.AwaitBeforeExecute"/> for why that is safe and is not a hedge being dropped.
/// </para>
/// <para>
/// The three rules share one traversal because they share one question - what is being done with the task -
/// and differ only in the answer. The receiver decides which world we are in: on a transaction or batch
/// nothing has been sent yet, so *any* wait is the problem; elsewhere the call is already in flight, and only
/// blocking is. Fire-and-forget cuts across both, because there the value is settled before the call returns.
/// </para>
/// <para>
/// The shapes covered are the ones that are certain without any flow analysis, because the wait is written at
/// the queueing site and so cannot be reached after an <c>Execute</c> of that same command:
/// </para>
/// <list type="bullet">
/// <item><description><c>await tran.StringGetAsync(key)</c></description></item>
/// <item><description><c>tran.StringGetAsync(key).Result</c></description></item>
/// <item><description><c>tran.StringGetAsync(key).Wait()</c>, and <c>.GetAwaiter().GetResult()</c></description></item>
/// </list>
/// <para>
/// A task captured to a local first is out of scope on purpose: awaiting one *after* <c>Execute</c> is the
/// recommended fix, so telling the two apart needs ordering, and an error that gets ordering wrong condemns
/// correct code. The same goes for <c>Task.WhenAll(...)</c> over queued commands.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueuedResultAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
        = ImmutableArray.Create(
            Diagnostics.AwaitBeforeExecute,
            Diagnostics.AwaitFireAndForgetResult,
            Diagnostics.BlockingOnRedisCall,
            Diagnostics.BlockingHelper);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // as TransactionAnalyzer: resolve once per compilation and bail, so the overwhelming majority of
        // compilations - which have never heard of this library - pay a couple of metadata lookups and nothing
        context.RegisterCompilationStartAction(static ctx =>
        {
            if (KnownSymbols.TryCreate(ctx.Compilation) is not { } known) return;

            // every shape we report is an await or a blocking read written directly around the queued call, so
            // the operation kinds are known up front and there is no need to walk whole blocks
            ctx.RegisterOperationAction(opCtx => Analyze(opCtx, known), OperationKind.Await, OperationKind.Invocation, OperationKind.PropertyReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context, KnownSymbols known)
    {
        // SER308 is a plain call rather than a wait wrapped around one, so it is answered first and on its
        // own terms: the blocking is inside the helper, and there is nothing here to unwrap
        if (context.Operation is IInvocationOperation helper && known.IsBlockingHelper(helper.TargetMethod))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.BlockingHelper,
                helper.Syntax.GetLocation(),
                helper.TargetMethod.Name));
            return;
        }

        // what is being waited for, and how - awaiting is correct usage nearly everywhere, blocking is not
        if (Waited(context.Operation, known) is not { } waited) return;
        if (waited.Call is not { } call || !known.IsRedis(call.Instance?.Type)) return;
        if (known.IsTerminator(call.TargetMethod)) return;

        var flags = FireAndForget(call, known);
        var queued = known.IsQueueing(call.Instance?.Type);

        var descriptor = (queued, flags, waited.Blocking) switch
        {
            // Queued on a transaction or batch: nothing has been sent, so the task cannot complete and this
            // waits forever - unless fire-and-forget already completed it. An unknown flags value picks
            // neither, because guessing wrong on SER305 is a build error on legal code.
            (true, FireAndForgetState.Set, _) => Diagnostics.AwaitFireAndForgetResult,
            (true, FireAndForgetState.Clear, _) => Diagnostics.AwaitBeforeExecute,
            (true, _, _) => null,

            // An ordinary redis call, already in flight: only *blocking* is a problem, and awaiting is exactly
            // what you should be doing. Blocking on fire-and-forget is its own case - it waits for nothing,
            // since the task was complete before the call returned - so it gets SER306's advice, not SER307's.
            //
            // Note the await of a fire-and-forget result is deliberately NOT reported here, though it is when
            // queued. `await db.KeyDeleteAsync(key, FireAndForget);` is a no-op await rather than a mistake,
            // and this repo alone contains 416 of them: a rule that noisy would be turned off wholesale, and
            // would take SER305 with it.
            (false, FireAndForgetState.Set, true) => Diagnostics.AwaitFireAndForgetResult,
            (false, _, true) => Diagnostics.BlockingOnRedisCall,
            (false, _, false) => null,
        };

        if (descriptor is null) return;

        // Reported on the whole wait - "await tran.StringGetAsync(key)" - because that expression is the thing
        // that is wrong; the call inside it is fine. The call is carried as an additional location so the code
        // fix can find it without re-deriving the unwrapping below in a second assembly.
        // only SER305 names the receiver ("queued on a transaction"); the other two are about the call itself,
        // and passing them a description they do not use would put "a batch" behind a plain database
        var args = ReferenceEquals(descriptor, Diagnostics.AwaitBeforeExecute)
            ? new object[] { call.TargetMethod.Name, Describe(call.Instance?.Type, known) }
            : new object[] { call.TargetMethod.Name };

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            context.Operation.Syntax.GetLocation(),
            additionalLocations: new[] { call.Syntax.GetLocation() },
            properties: null,
            args));
    }

    /// <summary>
    /// The operation whose result this one waits for, if it is one of the waiting shapes.
    /// </summary>
    /// <remarks>
    /// The three await-pattern members are matched by name rather than by symbol, which is what the language
    /// itself does for <c>GetAwaiter</c>/<c>GetResult</c> - and is safe regardless, because the caller still
    /// requires the unwrapped operation to be a redis call before anything is reported.
    /// <para>
    /// <c>Blocking</c> is the distinction that matters away from transactions: awaiting is correct usage of an
    /// async API, while blocking on it holds a thread for the round-trip (SER307).
    /// </para>
    /// </remarks>
    private static (IInvocationOperation? Call, bool Blocking)? Waited(IOperation operation, KnownSymbols known) => operation switch
    {
        // await tran.StringGetAsync(key)
        IAwaitOperation await => (Unwrap(await.Operation) as IInvocationOperation, false),

        // db.StringGetAsync(key).Result
        IPropertyReferenceOperation { Property.Name: "Result", Instance: { } instance } when known.IsTask(instance.Type)
            => (Unwrap(instance) as IInvocationOperation, true),

        // db.StringGetAsync(key).Wait()
        IInvocationOperation { TargetMethod.Name: "Wait", Instance: { } instance } when known.IsTask(instance.Type)
            => (Unwrap(instance) as IInvocationOperation, true),

        // db.StringGetAsync(key).GetAwaiter().GetResult()
        IInvocationOperation { TargetMethod.Name: "GetResult", Instance: IInvocationOperation { TargetMethod.Name: "GetAwaiter", Instance: { } instance } }
            => (Unwrap(instance) as IInvocationOperation, true),

        _ => null,
    };

    /// <summary>
    /// Strips the conversions the compiler may have inserted around the awaited expression.
    /// </summary>
    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true, Operand: { } operand }) operation = operand;
        return operation;
    }

    /// <summary>Whether the call carries <c>CommandFlags.FireAndForget</c>, if that can be known.</summary>
    private enum FireAndForgetState
    {
        /// <summary>The flags are known and do not include it: waiting here can never complete.</summary>
        Clear,

        /// <summary>The flags are known to include it: waiting completes, with nothing in it.</summary>
        Set,

        /// <summary>The flags are not a compile-time constant, so neither rule can be claimed.</summary>
        Unknown,
    }

    private static FireAndForgetState FireAndForget(IInvocationOperation invocation, KnownSymbols known)
    {
        if (known.FireAndForgetValue is not { } fireAndForget) return FireAndForgetState.Unknown;

        foreach (var argument in invocation.Arguments)
        {
            if (!known.IsCommandFlags(argument.Parameter?.Type)) continue;

            // An omitted argument is the parameter's default, which is CommandFlags.None throughout the API;
            // ConstantValue reports that for us rather than it being assumed here. Enums arrive as their
            // underlying value, and CommandFlags is int-backed; "A | B" over two constants is itself a
            // constant, so the fully-spelled-out combined forms land here too.
            var value = Unwrap(argument.Value);
            if (value.ConstantValue is { HasValue: true, Value: int constant })
            {
                return (constant & fireAndForget) != 0 ? FireAndForgetState.Set : FireAndForgetState.Clear;
            }

            return CarriesFireAndForget(value, fireAndForget) ? FireAndForgetState.Set : FireAndForgetState.Unknown;
        }

        // no flags parameter at all: nothing can have asked for fire-and-forget
        return FireAndForgetState.Clear;
    }

    /// <summary>
    /// Whether a flags expression that is not a constant is nonetheless certain to carry fire-and-forget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the obvious shape - <c>flags | CommandFlags.FireAndForget</c>, and chains of it - because <c>|</c>
    /// can only ever *set* bits, so a constant operand carrying the flag settles the question whatever the rest
    /// of the expression turns out to hold. Anything less obvious is left as unknown, which is silence; this is
    /// deliberately not an expression evaluator.
    /// </para>
    /// <para>
    /// <b>One-directional on purpose, and it must stay that way.</b> There is no matching treatment of <c>&amp;</c>
    /// or <c>~</c> to prove the flag *absent*, tempting as the symmetry looks: concluding "set" only ever
    /// produces <see cref="Diagnostics.AwaitFireAndForgetResult"/>, a warning, whereas concluding "clear"
    /// produces <see cref="Diagnostics.AwaitBeforeExecute"/> - an error, on a build, from an expression we only
    /// partly understood. The cheap half of this problem is also the safe half.
    /// </para>
    /// </remarks>
    private static bool CarriesFireAndForget(IOperation operation, int fireAndForget)
    {
        operation = Unwrap(operation);

        if (operation.ConstantValue is { HasValue: true, Value: int value }) return (value & fireAndForget) != 0;

        return operation is IBinaryOperation { OperatorKind: BinaryOperatorKind.Or } binary
            && (CarriesFireAndForget(binary.LeftOperand, fireAndForget)
                || CarriesFireAndForget(binary.RightOperand, fireAndForget));
    }

    /// <summary>How to name the receiver in the message.</summary>
    /// <remarks>
    /// Worth the words: "a transaction" and "a batch" behave identically here but read very differently to
    /// somebody who only knows they used one of them, and <c>ITransaction</c> is both.
    /// </remarks>
    private static string Describe(ITypeSymbol? type, KnownSymbols known)
        => known.IsTransactional(type) ? "a transaction" : "a batch";

    private sealed class KnownSymbols
    {
        private KnownSymbols(List<IMethodSymbol> blockingHelpers, INamedTypeSymbol? redisAsync, INamedTypeSymbol? batch, INamedTypeSymbol? transactionAsync, INamedTypeSymbol? transaction, INamedTypeSymbol? commandFlags, INamedTypeSymbol? task, int? fireAndForgetValue)
        {
            BlockingHelpers = blockingHelpers;
            RedisAsync = redisAsync;
            Batch = batch;
            TransactionAsync = transactionAsync;
            Transaction = transaction;
            CommandFlags = commandFlags;
            Task = task;
            FireAndForgetValue = fireAndForgetValue;
        }

        /// <summary>
        /// <c>Wait</c>/<c>WaitAll</c>/<c>TryWait</c>, gathered from both declaring interfaces.
        /// </summary>
        /// <remarks>
        /// Two unrelated interfaces declare these - IRedisAsync for database/server/subscriber calls, and
        /// IConnectionMultiplexer for its own - so one lookup would silently cover only half the surface.
        /// </remarks>
        private List<IMethodSymbol> BlockingHelpers { get; }

        /// <summary>The root of every async redis surface: IDatabaseAsync, IServer and ISubscriber all derive from it.</summary>
        private INamedTypeSymbol? RedisAsync { get; }

        private INamedTypeSymbol? Batch { get; }
        private INamedTypeSymbol? TransactionAsync { get; }
        private INamedTypeSymbol? Transaction { get; }
        private INamedTypeSymbol? CommandFlags { get; }
        private INamedTypeSymbol? Task { get; }

        /// <summary>
        /// The value of <c>CommandFlags.FireAndForget</c>, read from the referenced library rather than assumed.
        /// </summary>
        public int? FireAndForgetValue { get; }

        public static KnownSymbols? TryCreate(Compilation compilation)
        {
            // IBatch and ITransactionAsync are the two roots of the queueing surface, and neither derives from
            // the other: ITransaction implements both, while IBatch is reachable on its own from CreateBatch.
            // No queueing type at all => not our library, or not a version with one; either way, nothing here.
            var redisAsync = compilation.GetTypeByMetadataName("StackExchange.Redis.IRedisAsync");
            var batch = compilation.GetTypeByMetadataName("StackExchange.Redis.IBatch");
            var transactionAsync = compilation.GetTypeByMetadataName("StackExchange.Redis.ITransactionAsync");
            if (redisAsync is null && batch is null && transactionAsync is null) return null;

            var commandFlags = compilation.GetTypeByMetadataName("StackExchange.Redis.CommandFlags");
            int? fireAndForget = null;
            if (commandFlags is not null)
            {
                foreach (var member in commandFlags.GetMembers("FireAndForget"))
                {
                    if (member is IFieldSymbol { HasConstantValue: true, ConstantValue: int value })
                    {
                        fireAndForget = value;
                        break;
                    }
                }
            }

            var blockingHelpers = new List<IMethodSymbol>();
            foreach (var declaring in new[] { redisAsync, compilation.GetTypeByMetadataName("StackExchange.Redis.IConnectionMultiplexer") })
            {
                if (declaring is null) continue;
                foreach (var member in declaring.GetMembers())
                {
                    if (member is IMethodSymbol { Name: "Wait" or "WaitAll" or "TryWait" } helper) blockingHelpers.Add(helper);
                }
            }

            return new KnownSymbols(
                blockingHelpers,
                redisAsync,
                batch,
                transactionAsync,
                compilation.GetTypeByMetadataName("StackExchange.Redis.ITransaction"),
                commandFlags,
                compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
                fireAndForget);
        }

        /// <summary>
        /// Whether this is one of the library's own blocking helpers - <c>Wait</c>, <c>WaitAll</c>,
        /// <c>TryWait</c> - on either of the two unrelated interfaces that declare them.
        /// </summary>
        /// <remarks>
        /// Matched through <c>FindImplementationForInterfaceMember</c> as well as directly, so a call on a
        /// class rather than an interface still counts. That is not a corner case here: ConnectionMultiplexer
        /// is what Connect returns, so <c>conn.Wait(task)</c> is the common shape and its containing type is
        /// the class.
        /// </remarks>
        public bool IsBlockingHelper(IMethodSymbol method)
        {
            if (BlockingHelpers.Count == 0) return false;
            if (method.Name is not ("Wait" or "WaitAll" or "TryWait")) return false; // cheap reject first

            foreach (var helper in BlockingHelpers)
            {
                if (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, helper)) return true;
            }

            var containing = method.ContainingType;
            if (containing is null) return false;
            foreach (var helper in BlockingHelpers)
            {
                if (SymbolEqualityComparer.Default.Equals(containing.FindImplementationForInterfaceMember(helper), method.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether this receiver is any asynchronous redis surface at all.</summary>
        public bool IsRedis(ITypeSymbol? type) => Implements(type, RedisAsync);

        /// <summary>Whether commands on this receiver are queued rather than sent.</summary>
        public bool IsQueueing(ITypeSymbol? type)
            => Implements(type, Batch) || Implements(type, TransactionAsync);

        /// <summary>Whether this receiver is a transaction, as opposed to a plain batch.</summary>
        public bool IsTransactional(ITypeSymbol? type)
            => Implements(type, TransactionAsync) || Implements(type, Transaction);

        public bool IsCommandFlags(ITypeSymbol? type)
            => CommandFlags is not null && SymbolEqualityComparer.Default.Equals(type, CommandFlags);

        /// <summary><c>Task</c> or <c>Task&lt;T&gt;</c>, which is all the queueing surface returns.</summary>
        /// <remarks>
        /// Walks the base chain rather than testing one type, because <c>Task&lt;T&gt;</c> is not <c>Task</c> -
        /// it derives from it - and <c>.Result</c> is declared on the generic one.
        /// </remarks>
        public bool IsTask(ITypeSymbol? type)
        {
            if (Task is null) return false;
            for (var candidate = type as INamedTypeSymbol; candidate is not null; candidate = candidate.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate.ConstructedFrom, Task)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether this is one of the methods that ends the batch, rather than a command queued into it.
        /// </summary>
        /// <remarks>
        /// Decided by *declaring interface*, never by name, and that is the whole trap in this rule.
        /// <c>ITransaction[Async].ExecuteAsync(CommandFlags)</c> is the one call here that must be awaited,
        /// while <c>IDatabaseAsync.ExecuteAsync(string command, ...)</c> - the raw-command escape hatch - is an
        /// ordinary queued command that must still be reported. A name match on "ExecuteAsync" gets both of
        /// those exactly the wrong way round.
        /// <para>
        /// The receiver is not always the interface, though: a call on a *class* that implements it - a
        /// decorator such as <c>KeyPrefixedTransaction</c>, or a caller's own wrapper - resolves to the class's
        /// member, whose containing type is the class. So where the direct test fails, ask the type which of
        /// its members implements each terminator. This repo's own build found that one, which is a fair
        /// preview of how a consumer would have.
        /// </para>
        /// </remarks>
        public bool IsTerminator(IMethodSymbol method)
        {
            var containing = method.ContainingType;
            if (containing is null) return false;
            if (Same(containing, Batch) || Same(containing, TransactionAsync) || Same(containing, Transaction)) return true;

            foreach (var iface in containing.AllInterfaces)
            {
                if (!Same(iface, Batch) && !Same(iface, TransactionAsync) && !Same(iface, Transaction)) continue;

                foreach (var member in iface.GetMembers())
                {
                    if (member is IMethodSymbol interfaceMethod
                        && SymbolEqualityComparer.Default.Equals(containing.FindImplementationForInterfaceMember(interfaceMethod), method))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Same(ITypeSymbol? type, INamedTypeSymbol? target)
            => target is not null && SymbolEqualityComparer.Default.Equals(type, target);

        private static bool Implements(ITypeSymbol? type, INamedTypeSymbol? target)
        {
            if (type is null || target is null) return false;
            if (SymbolEqualityComparer.Default.Equals(type, target)) return true;

            foreach (var iface in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, target)) return true;
            }

            return false;
        }
    }
}
