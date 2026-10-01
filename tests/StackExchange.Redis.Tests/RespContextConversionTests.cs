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
    /// The base interface still exposes the plain context, because it is the extensibility seam.
    /// </summary>
    /// <remarks>
    /// An extender holding an <see cref="IRespTarget"/> genuinely does need the plumbing, and that is a
    /// different question from whether a concrete context advertises it.
    /// </remarks>
    [Fact]
    public void TheBaseInterfaceStillExposesThePlainContext()
    {
        Assert.Equal(typeof(RespContext), typeof(IRespTarget).GetProperty("Context")!.PropertyType);

        IRespTarget target = new RespDatabaseContext(new RespContext().WithDatabase(4));
        Assert.Equal(4, target.Context.Database);
    }

    /// <summary>
    /// The derived interfaces <b>hide</b> it, so the typed context is what a caller gets by default.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the <c>new</c>: <c>db.Context</c> should be the thing that knows whether
    /// <c>Strings</c> makes sense, and the plain one should take deliberately asking for the base
    /// interface. A reflection check alone would pass if the hiding were removed and the members merely
    /// coexisted, so the assignments below are the real assertion - they only compile while it holds.
    /// </remarks>
    [Fact]
    public void TheDerivedInterfacesHideItWithTheTypedOne()
    {
        Assert.Equal(typeof(RespDatabaseContext), typeof(IRespKeyspaceTarget).GetProperty("Context")!.PropertyType);
        Assert.Equal(typeof(RespServerContext), typeof(IRespServerTarget).GetProperty("Context")!.PropertyType);

        IRespKeyspaceTarget keyspace = new RespDatabaseContextTarget(new RespContext().WithDatabase(9));
        RespDatabaseContext typed = keyspace.Context; // would not compile if the base member won
        Assert.Equal(9, typed.Database);

        IRespTarget asBase = keyspace;
        Assert.Equal(9, asBase.Context.Database);
    }

    /// <summary>A minimal keyspace target, standing in for IDatabase without needing a connection.</summary>
    private sealed class RespDatabaseContextTarget(RespContext context) : IRespKeyspaceTarget
    {
        public RespDatabaseContext Context => new(context);

        RespContext IRespTarget.Context => context;
    }

    /// <summary>
    /// And a command group hands nothing back either: it is a context plus a name, and the name is the
    /// point.
    /// </summary>
    /// <remarks>
    /// Checked by reflection over every group at once, because the interesting failure is a new group
    /// being added with the old public <c>Context</c> property - which no compile-time check would catch
    /// and which would quietly reopen the hole on that one type.
    /// </remarks>
    [Fact]
    public void NoGroupExposesItsContext()
    {
        var groups = typeof(RespStrings).Assembly.GetExportedTypes()
            .Where(t => t.IsValueType && t.Name.StartsWith("Resp", StringComparison.Ordinal) && t.Name != nameof(RespContext))
            .Where(t => t.GetField("Context", BindingFlags.NonPublic | BindingFlags.Instance) is not null)
            .ToArray();

        Assert.NotEmpty(groups); // a reflection typo would otherwise pass vacuously
        Assert.All(groups, t =>
        {
            Assert.Null(t.GetProperty("Context", BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(t.GetField("Context", BindingFlags.Public | BindingFlags.Instance));
        });
    }

    [Fact]
    public void TheCastRoundTrips()
    {
        var context = new RespContext().WithDatabase(7);

        Assert.Equal(7, ((RespContext)new RespDatabaseContext(context)).Database);
        Assert.Equal(7, ((RespContext)new RespServerContext(context)).Database);
    }
}
