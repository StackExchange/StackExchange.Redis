using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using RESPite.Operations;
using Xunit;

namespace RESPite.Tests;

/// <summary>
/// The net under the timeout: an operation nobody ever completes must not wait for ever.
/// </summary>
/// <remarks>
/// <see cref="RespMessageBase{TResponse}"/> had a timeout that only the SYNCHRONOUS wait could reach, so a
/// synchronous caller timed out and an asynchronous one simply never completed. Two separate hangs in the
/// new core's suite were that, and each cost hours to find because a process that waits for ever says
/// nothing about why.
/// </remarks>
[Collection(nameof(OperationBackstopTests))]
[CollectionDefinition(nameof(OperationBackstopTests), DisableParallelization = true)]
public class OperationBackstopTests
{
    private sealed class NeverAnswered : RespMessageBase<string?>
    {
        public NeverAnswered() : base(RespParseOptions.Parse) { }

        protected override string? Parse(ref RespReader reader) => null;

        internal NeverAnswered Arm(CancellationToken cancellationToken = default)
        {
            SetRequest(Encoding.UTF8.GetBytes("*1\r\n$4\r\nPING\r\n"), null, cancellationToken);
            Assert.True(TryReserveRequest(Token, out _));
            ReleaseRequest();
            return this;
        }
    }

    /// <summary>Run with a backstop short enough to test, then put it back.</summary>
    private static async Task WithBackstopAsync(TimeSpan backstop, TimeSpan period, Func<Task> body)
    {
        var priorBackstop = OperationBackstop.Backstop;
        var priorPeriod = OperationBackstop.Period;
        OperationBackstop.Backstop = backstop;
        OperationBackstop.Period = period;
        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            OperationBackstop.Backstop = priorBackstop;
            OperationBackstop.Period = priorPeriod;
        }
    }

    [Fact]
    public async Task AnOperationNobodyAnswersEventuallyTimesOut()
        => await WithBackstopAsync(TimeSpan.FromMilliseconds(150), TimeSpan.Zero, async () =>
        {
            var message = new NeverAnswered().Arm();
            var pending = new RespOperation<string?>(message).AsTask();

            var completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.True(ReferenceEquals(completed, pending), "the backstop did not fire; the operation would have hung");

            // A TIMEOUT, not a cancellation. The distinction is load-bearing: a cancellation is a definite
            // outcome and says the command did not happen, which for a deadline is not known and would
            // cost a duplicate write if retry believed it.
            await Assert.ThrowsAsync<TimeoutException>(() => pending).ConfigureAwait(false);
        });

    [Fact]
    public async Task ACallerWhoSuppliedATokenIsLeftAlone()
        => await WithBackstopAsync(TimeSpan.FromMilliseconds(150), TimeSpan.Zero, async () =>
        {
            using var caller = new CancellationTokenSource();
            var message = new NeverAnswered().Arm(caller.Token);
            var pending = new RespOperation<string?>(message).AsTask();

            // the caller said how long they are willing to wait; the backstop must not second-guess it
            var completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMilliseconds(600))).ConfigureAwait(false);
            Assert.False(ReferenceEquals(completed, pending), "the backstop overrode the caller's own token");

            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending).ConfigureAwait(false);
        });

    [Fact]
    public async Task TheBackstopCanBeTurnedOff()
        => await WithBackstopAsync(TimeSpan.Zero, TimeSpan.Zero, async () =>
        {
            var message = new NeverAnswered().Arm();
            var pending = new RespOperation<string?>(message).AsTask();

            var completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMilliseconds(500))).ConfigureAwait(false);
            Assert.False(ReferenceEquals(completed, pending), "disabled means disabled");
        });
}
