using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Default handler lookup: implementing the interface is the registration.
/// </summary>
/// <remarks>
/// Replaces a <c>typeof(T) ==</c> ladder that had grown to 25 entries and that nobody was obliged to
/// extend. These pin the properties that make the replacement worth having.
/// </remarks>
public class RespHandlerRegistryTests
{
    private static readonly Type Defaults = typeof(RespHandlers)
        .GetNestedTypes(System.Reflection.BindingFlags.NonPublic)
        .Single(t => t.Name == "DefaultHandlers");

    [Fact]
    public void EveryDefaultIsRegisteredByImplementingTheInterface()
    {
        // the registration IS the interface list, so this is the whole registry
        var registered = Defaults.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition().Name.StartsWith("IResp", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(registered);

        // and each one really resolves through the public entry point
        foreach (var iface in registered.Where(i => i.GetGenericTypeDefinition() == typeof(IRespHandler<>)))
        {
            var t = iface.GetGenericArguments()[0];
            var inbuilt = typeof(RespHandlers).GetNestedType("Inbuilt`1", System.Reflection.BindingFlags.NonPublic)!
                .MakeGenericType(t);
            var handler = inbuilt.GetField("Handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null);

            Assert.True(handler is not null, $"no default resolved for {t.Name}, yet DefaultHandlers implements it");
        }
    }

    [Fact]
    public void TwoDefaultsForOneTypeWouldBeACompileError()
    {
        // not assertable directly - it is a compile error, which is the point. What IS assertable is the
        // consequence: types with more than one handler keep exactly one on the defaults object, and the
        // alternates stay separate and are named explicitly by whoever wants them.
        Assert.NotSame(RespHandlers.Boolean, RespHandlers.Success);
        Assert.NotSame(RespHandlers.Value, RespHandlers.SingletonValue);
        Assert.NotSame(RespHandlers.Lease, RespHandlers.SingletonLease);

        // the defaults are the ones on the singleton; the alternates are not
        Assert.Equal(Defaults, RespHandlers.Boolean.GetType());
        Assert.Equal(Defaults, RespHandlers.Value.GetType());
        Assert.NotEqual(Defaults, RespHandlers.Success.GetType());
        Assert.NotEqual(Defaults, RespHandlers.SingletonValue.GetType());
    }

    [Fact]
    public void AnUnregisteredTypeSaysSoRatherThanGuessing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => RespHandlers.Inbuilt<Guid>.Require());
        Assert.Contains("Guid", ex.Message);
    }

    [Fact]
    public void InvarianceStopsABaseTypeBorrowingADerivedHandler()
    {
        // IRespHandler<T> must stay invariant. Were it covariant, `as IRespHandler<object>` would match the
        // string handler and SendAsync<object> would silently get a string parser - the one hazard this
        // lookup has that a typeof ladder did not.
        Assert.False(typeof(IRespHandler<>).GetGenericArguments()[0].GenericParameterAttributes
            .HasFlag(System.Reflection.GenericParameterAttributes.Covariant));

        Assert.NotNull(RespHandlers.Inbuilt<string>.Handler);
        Assert.Null(RespHandlers.Inbuilt<object>.Handler);
    }

    [Fact]
    public async Task TheResolvedHandlersStillParse()
    {
        // a spot check that the bodies survived being moved onto one object
        var executor = new RespHandlerRegistryExecutor("$5\r\nhello\r\n", ":42\r\n", "+OK\r\n");
        var context = new RespContext().WithExecutor(executor);

        Assert.Equal("hello", await context.SendAsync<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None));
        Assert.Equal(42, await context.SendAsync<long>($"{RedisCommand.INCR}{(RedisKey)"k"}", CommandFlags.None));
        Assert.True(await context.SendAsync<bool>($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}", CommandFlags.None));
    }
}

internal sealed class RespHandlerRegistryExecutor(params string[] replies) : IRespExecutor
{
    private int _next;

    public int Database => 0;

    public RespPayload Send(in RespRequest request)
        => RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));

    public ValueTask<RespPayload> SendAsync(RespRequest request, System.Threading.CancellationToken cancellationToken = default)
        => new(Send(request));
}
