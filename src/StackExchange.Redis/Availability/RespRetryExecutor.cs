using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Retry, as a decorator on the <b>executor</b> rather than a wrapper on the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same policy as <see cref="RetryDatabase"/>, and a fraction of the code.</b> That one has to
/// capture every argument of every command into a generated state struct so it can replay the call, own
/// pooled copies of any <c>Memory&lt;T&gt;</c> arguments for as long as the replaying lasts, and dispose
/// the capture when the last attempt is done - because what it is replaying is a <i>method call</i>. Here
/// the thing being replayed is a rendered frame that already exists, so there is nothing to capture,
/// nothing to own and nothing to map: the loop is the whole of it. Both call the same
/// <see cref="RetryController"/>, so the two paths cannot disagree about policy.
/// </para>
/// <para>
/// <b>Asynchronous only</b>, for the reason <see cref="RetryDatabase"/> gives for implementing only
/// <see cref="IDatabaseAsync"/>: retry is inherently delay-ish, and every pause the controller asks for is
/// a <see cref="Task.Delay(int)"/>. A synchronous send through a retrying context therefore throws rather
/// than quietly getting no retry - which would be the same invisible loss that stops
/// <c>RetryDatabase.GetContextCore</c> forwarding the inner context.
/// </para>
/// <para>
/// <b>Failover is not wired up yet.</b> <see cref="RetryDatabase"/> takes its next-failover token from
/// <c>IDatabaseAsync.GetNextFailover()</c>; an executor has no equivalent, so this takes an optional
/// source and is built without <see cref="DatabaseFeatureFlags.Failover"/> when there is none - which
/// makes <see cref="RetryController.TracksFailover"/> false and the failover rungs unreachable, rather
/// than silently pretending a failover could happen.
/// </para>
/// </remarks>
internal sealed class RespRetryExecutor : IRespExecutor, IRespPreambleExecutor
{
    private readonly IRespExecutor _inner;
    private readonly RetryController _controller;
    private readonly Func<CancellationToken>? _failoverSource;

    internal RespRetryExecutor(IRespExecutor inner, RetryPolicy policy, Func<CancellationToken>? failoverSource = null)
        : this(
            inner,
            new RetryController(
                policy ?? throw new ArgumentNullException(nameof(policy)),
                failoverSource is null ? DatabaseFeatureFlags.None : DatabaseFeatureFlags.Failover),
            failoverSource)
    {
    }

    /// <summary>Share an existing controller, rather than deriving a second one from the same policy.</summary>
    /// <remarks>
    /// For <see cref="RetryDatabase"/>, whose context is this executor over the inner one: sharing the
    /// controller means the database path and the context path are not merely configured alike, they are
    /// configured by the same object - including the failover feature flag, which a policy alone does not
    /// carry.
    /// </remarks>
    internal RespRetryExecutor(IRespExecutor inner, RetryController controller, Func<CancellationToken>? failoverSource)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _failoverSource = failoverSource;
    }

    /// <summary>The policy in force; read by the tests, which assert how a policy was resolved.</summary>
    internal RetryPolicy Policy => _controller.Policy;

    /// <summary>The executor underneath, so a second <c>WithRetry</c> can refuse rather than nest.</summary>
    internal IRespExecutor Inner => _inner;

    public int Database => _inner.Database;

    /// <inheritdoc/>
    /// <remarks><inheritdoc cref="RespRetryExecutor" path="/remarks/para[2]"/></remarks>
    public RespPayload Send(in RespRequest request) => throw NoSynchronousRetry();

    internal static InvalidOperationException NoSynchronousRetry() => new(
        "A retrying context has no synchronous send: every pause a retry takes is asynchronous. "
        + "Use the asynchronous surface, or compose the command from a context without retry.");

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Two short-circuits, and both hand back the inner task untouched.</b> A controller that can never
    /// retry is a pure forward - no loop, no state machine, nothing of this class on the path at all - and
    /// a send that completed synchronously and successfully has nothing left to retry. Only a fault or a
    /// real await reaches <see cref="Awaited"/>.
    /// </remarks>
    public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
    {
        if (!_controller.CanEverRetry) return _inner.SendAsync(request, cancellationToken);

        // start the first attempt eagerly, so a send that completes synchronously and successfully - a
        // cache hit, a fake - costs no state machine at all
        var pending = _inner.SendAsync(request, cancellationToken);
        return pending.IsCompletedSuccessfully ? pending : Awaited(pending, request, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Forwarded rather than declined</b>, so a script or a field-set import composed from a retrying
    /// context still travels as a pair. Without this the executor would not be an
    /// <see cref="IRespPreambleExecutor"/> and <c>RespExecutor.AwaitPair</c> would take its sequential
    /// fallback: still correct, but an extra round trip, and the gate is not consulted - so an
    /// <c>EVALSHA</c> would carry a <c>SCRIPT LOAD</c> it did not need on every call.
    /// </remarks>
    public ValueTask<RespPayload> SendAsync(RespRequest preamble, RespRequest request, IRespPreambleGate? gate, CancellationToken cancellationToken = default)
    {
        if (_inner is not IRespPreambleExecutor pairs)
        {
            // the inner one cannot write a pair either; let the caller's own fallback handle it
            throw new NotSupportedException("The inner executor does not support preambles.");
        }

        if (!_controller.CanEverRetry) return pairs.SendAsync(preamble, request, gate, cancellationToken);

        var pending = pairs.SendAsync(preamble, request, gate, cancellationToken);
        return pending.IsCompletedSuccessfully ? pending : AwaitedPair(pending, pairs, preamble, request, gate, cancellationToken);
    }

    /// <summary>The retry loop, borrowed whole from <see cref="RetryDatabase"/>.</summary>
    /// <remarks>
    /// <para>
    /// Every decision is the controller's: whether the fault is transient, whether attempts remain,
    /// whether this is the rung that should fail over, and how long to wait. What is left here is the
    /// shape of the loop.
    /// </para>
    /// <para>
    /// <b>The failover token is captured before the first attempt is awaited</b>, as it is there and for
    /// the same reason: taken afterwards, a failover happening between the failure and the fetch would be
    /// missed, and the retry would wait for one that had already been and gone.
    /// </para>
    /// </remarks>
    private async ValueTask<RespPayload> Awaited(ValueTask<RespPayload> pending, RespRequest request, CancellationToken cancellationToken)
    {
        int attempt = 0;
        CancellationToken failover = GetNextFailover();
        while (true)
        {
            try
            {
                return await pending.ConfigureAwait(false);
            }
            catch (Exception ex) when (_controller.CanRetry(++attempt, ex, ref failover, out var delay))
            {
                await _controller.FailoverOrDelayAsync(delay).ConfigureAwait(false);
            }

            pending = _inner.SendAsync(request, cancellationToken);
        }
    }

    /// <inheritdoc cref="Awaited"/>
    private async ValueTask<RespPayload> AwaitedPair(
        ValueTask<RespPayload> pending,
        IRespPreambleExecutor pairs,
        RespRequest preamble,
        RespRequest request,
        IRespPreambleGate? gate,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        CancellationToken failover = GetNextFailover();
        while (true)
        {
            try
            {
                return await pending.ConfigureAwait(false);
            }
            catch (Exception ex) when (_controller.CanRetry(++attempt, ex, ref failover, out var delay))
            {
                await _controller.FailoverOrDelayAsync(delay).ConfigureAwait(false);
            }

            // the gate is re-consulted on each attempt, which is what makes replaying a pair correct: a
            // retry after a dropped connection lands on a fresh one, where the preamble is needed again
            pending = pairs.SendAsync(preamble, request, gate, cancellationToken);
        }
    }

    private CancellationToken GetNextFailover()
        => _controller.TracksFailover && _failoverSource is not null
            ? _failoverSource()
            : CancellationToken.None;
}
