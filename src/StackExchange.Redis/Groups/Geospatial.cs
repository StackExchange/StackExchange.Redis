using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The geospatial commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Geospatial.cs</c> holds the group type and the accessor that reaches it, <c>Geospatial.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Geospatial</c> inside a class named
/// <c>Geospatial</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
[Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]

// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Geospatial
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The geospatial group: <c>target.Geospatial.AddAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A sorted set with coordinates for scores, which is why <c>GeoRemove</c> is <c>ZREM</c> - there is no
/// <c>GEOREM</c>, and there never was.
/// </para>
/// <para>
/// The group's own method is <see cref="Geospatial.SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags, CancellationToken)"/>;
/// <c>GEORADIUS</c> has no method here at all, exactly as <c>GETSET</c> and <c>RPOPLPUSH</c> have none.
/// It survives as an internal sibling so that a caller of the old <c>GeoRadius</c> keeps working on a
/// server that predates <c>GEOSEARCH</c>.
/// </para>
/// </remarks>
[Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
public readonly struct RespGeospatial
{
    private readonly RespContext _context;

    /// <summary>Group the geospatial commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespGeospatial(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

public static partial class RespDatabaseExtensions
{
    extension(IRespKeyspaceTarget target)
    {
        /// <summary>The geospatial commands.</summary>
        public RespGeospatial Geospatial => new(target.Context);
    }

    extension(in RespContext context)
    {
        /// <summary>The geospatial commands.</summary>
        public RespGeospatial Geospatial => new(context);
    }
}
