using System;
using System.Collections.Generic;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What an interpolated command does when writing one of its arguments throws.
/// </summary>
/// <remarks>
/// <para>
/// <b>It drops the rented buffer on the floor, and that is accepted.</b> The handler rents in its
/// constructor, the compiler lowers the holes to calls made <i>after</i> that and <i>before</i> the method
/// the handler is being passed to, and a throw in the middle skips the rest - so nothing is in a position
/// to give the buffer back. Measured with an <c>ArrayPool</c> bucket fingerprint: a successful render
/// leaves the pool unchanged, a throwing one loses a 256-byte array.
/// </para>
/// <para>
/// <b><see cref="System.Runtime.CompilerServices.DefaultInterpolatedStringHandler"/> behaves identically</b>
/// - <c>$"...{x}..."</c> where <c>x.ToString()</c> throws loses a 256-char array by the same measurement.
/// So this is the language's existing bargain rather than something this library invented, and a fault
/// while building a command is already exceptional. Recorded so that nobody claims the write path is
/// exception-safe: it is not, deliberately.
/// </para>
/// <para>
/// The pool-identity measurement is not kept as an assertion - it reads <c>ArrayPool</c> internals and
/// would flake under parallel runs - but the evaluation order below is what makes the leak unavoidable,
/// and that is worth pinning.
/// </para>
/// </remarks>
public class RespHandlerFaultTests
{
    private sealed class Recording(List<string> written, string name) : IRespArgument
    {
        public void WriteTo(scoped ref RespCommandHandler handler)
        {
            written.Add(name);
            handler.AppendFormatted((RedisValue)name);
        }
    }

    private sealed class Throwing(List<string> written) : IRespArgument
    {
        public void WriteTo(scoped ref RespCommandHandler handler)
        {
            written.Add("boom");
            throw new InvalidOperationException("operand failed");
        }
    }

    /// <summary>
    /// Arguments are evaluated and written <b>one at a time</b>, not gathered up first.
    /// </summary>
    /// <remarks>
    /// This is why a faulting argument cannot be made safe by the call site: by the time it throws, the
    /// handler has been constructed and partly written, and the operands after it have not run at all.
    /// Any scheme that wanted to clean up would have to wrap the lowered call, which is not something a
    /// library can do.
    /// </remarks>
    [Fact]
    public void ArgumentsAreWrittenOneAtATime()
    {
        var ctx = new RespContext();
        var written = new List<string>();

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var frame = ctx.Render(
                $"{RedisCommand.GET}{new Recording(written, "first")}{new Throwing(written)}{new Recording(written, "third")}");
        });

        Assert.Equal(["first", "boom"], written);
    }
}
