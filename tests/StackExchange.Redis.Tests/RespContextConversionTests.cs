using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Getting at the plumbing behind a typed context is a <b>cast</b>, and an explicit one.
/// </summary>
/// <remarks>
/// <para>
/// It was a public <c>Raw</c> property, which made reaching past the semantics look like an ordinary part
/// of the surface when it is the opposite: a bare <see cref="RespContext"/> says nothing about whether
/// <c>Strings</c> or <c>Keyspace</c> is a sensible thing to ask for, so stepping out of the typed world is
/// something a caller should do on purpose and a reader should notice.
/// </para>
/// <para>
/// <b>Explicit rather than implicit is the half that needs a test</b>, because nothing else would catch
/// it changing: an implicit conversion would compile every call site that exists today and would also let
/// a context slide into a <see cref="RespContext"/> parameter during overload resolution, silently.
/// </para>
/// </remarks>
public class RespContextConversionTests
{
    public static TheoryData<Type> ContextTypes() => new()
    {
        typeof(RespDatabaseContext),
        typeof(RespServerContext),
    };

    [Theory]
    [MemberData(nameof(ContextTypes))]
    public void TheConversionIsExplicitAndNotImplicit(Type contextType)
    {
        var ops = contextType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(RespContext))
            .ToArray();

        Assert.Contains(ops, m => m.Name == "op_Explicit");
        Assert.DoesNotContain(ops, m => m.Name == "op_Implicit");
    }

    /// <summary>And <c>Raw</c> is not a public member any more, on either of them.</summary>
    [Theory]
    [MemberData(nameof(ContextTypes))]
    public void RawIsNotPublic(Type contextType)
    {
        Assert.Null(contextType.GetProperty("Raw", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(contextType.GetField("Raw", BindingFlags.Public | BindingFlags.Instance));
    }

    /// <summary>
    /// The interface still exposes it, because the interface is the extensibility seam.
    /// </summary>
    /// <remarks>
    /// An extender holding an <see cref="IRespTarget"/> - an <see cref="IDatabase"/>, say - genuinely does
    /// need the plumbing, and that is a different question from whether a concrete context advertises it.
    /// </remarks>
    [Fact]
    public void TheInterfaceStillExposesIt()
    {
        Assert.NotNull(typeof(IRespTarget).GetProperty("Raw"));

        IRespTarget target = new RespDatabaseContext(new RespContext().WithDatabase(4));
        Assert.Equal(4, target.Raw.Database);
    }

    [Fact]
    public void TheCastRoundTrips()
    {
        var context = new RespContext().WithDatabase(7);

        Assert.Equal(7, ((RespContext)new RespDatabaseContext(context)).Database);
        Assert.Equal(7, ((RespContext)new RespServerContext(context)).Database);
    }
}
