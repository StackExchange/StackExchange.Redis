using System;
using System.Linq;
using System.Reflection;
using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Reaches the new core's <b>private</b> nested handlers - most per-command handlers are private to the
/// group that sends the command, which <c>InternalsVisibleTo</c> does not open up.
/// </summary>
/// <remarks>
/// Reflection rather than a copy of the parse, so that what is tested is the instance the core really
/// sends with. A rename fails loudly here rather than silently testing something else.
/// </remarks>
internal static class PrivateHandlers
{
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The handler held by <paramref name="member"/> on <paramref name="owner"/>'s nested type <paramref name="nestedType"/>.</summary>
    internal static IRespHandler<T> Get<T>(Type owner, string nestedType, string member = "Instance")
        => Assert.IsAssignableFrom<IRespHandler<T>>(GetInstance(owner, nestedType, member));

    /// <summary>
    /// Parse with a handler whose result type is itself private (the handshake's reply structs), boxed,
    /// so that the test can read its members by reflection.
    /// </summary>
    internal static object? ParseBoxed(Type owner, string nestedType, string resp, string member = "Instance")
    {
        var handler = GetInstance(owner, nestedType, member);
        var resultType = handler.GetType().GetInterfaces()
            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRespHandler<>))
            .GetGenericArguments()[0];

        var parse = typeof(PrivateHandlers).GetMethod(nameof(ParseCore), AnyStatic)!.MakeGenericMethod(resultType);
        try
        {
            return parse.Invoke(null, [handler, Encoding.UTF8.GetBytes(resp)]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    /// <summary>Read a non-public instance property off a boxed result.</summary>
    internal static T? Property<T>(object? boxed, string name)
    {
        Assert.NotNull(boxed);
        var property = boxed.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return (T?)property.GetValue(boxed);
    }

    private static object? ParseCore<T>(IRespHandler<T> handler, byte[] resp) => RespExecutor.ParseFromSpan(handler, resp);

    private static object GetInstance(Type owner, string nestedType, string member)
    {
        var type = owner.GetNestedType(nestedType, BindingFlags.Public | BindingFlags.NonPublic);
        Assert.True(type is not null, $"{owner.Name}.{nestedType} not found");
        var value = type.GetField(member, AnyStatic)?.GetValue(null) ?? type.GetProperty(member, AnyStatic)?.GetValue(null);
        Assert.True(value is not null, $"{owner.Name}.{nestedType}.{member} not found");
        return value;
    }
}
