using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The geospatial group: <c>target.Geospatial.AddAsync(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sorted set with coordinates for scores, which is why <c>GeoRemove</c> is <c>ZREM</c> - there is no
    /// <c>GEOREM</c>, and there never was.
    /// </para>
    /// <para>
    /// The group's own method is <see cref="RespSurface.SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)"/>;
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

    public static partial class RespSurface
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

        /// <summary>GEOADD; the reply is whether the member was new.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The member and its position.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> AddAsync(this in RespGeospatial geo, RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync<bool>(
                $"{RedisCommand.GEOADD}{key}{(RedisValue)value.Longitude}{(RedisValue)value.Latitude}{value.Member}",
                flags);

        /// <summary>GEOADD with several members; the reply is how many were new.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="values">The members and their positions.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> AddAsync(this in RespGeospatial geo, RedisKey key, ReadOnlySpan<GeoEntry> values, CommandFlags flags = CommandFlags.None)
        {
            if (values.IsEmpty) return new ValueTask<long>(0L);

            var cmd = geo.Context.Compose(RedisCommand.GEOADD, 1 + (values.Length * 3));
            try
            {
                cmd.AppendFormatted(key);
                foreach (ref readonly var value in values)
                {
                    cmd.AppendFormatted((RedisValue)value.Longitude);
                    cmd.AppendFormatted((RedisValue)value.Latitude);
                    cmd.AppendFormatted(value.Member);
                }
            }
            catch
            {
                cmd.Dispose();
                throw;
            }

            var frame = cmd.Complete();
            return geo.Context.SendAsync(ref frame, flags, RespHandlers.Int64, default);
        }

        /// <summary>ZREM: a geo set is a sorted set, and removal is the sorted-set command.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="member">The member to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> RemoveAsync(this in RespGeospatial geo, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync<bool>(
                $"{RedisCommand.ZREM}{key}{member}", flags);

        /// <summary>GEODIST: how far apart two members are, or nil if either is missing.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member1">The first member.</param>
        /// <param name="member2">The second member.</param>
        /// <param name="unit">The unit to answer in.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<double?> DistanceAsync(
            this in RespGeospatial geo,
            RedisKey key,
            RedisValue member1,
            RedisValue member2,
            GeoUnit unit = GeoUnit.Meters,
            CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync<double?>(
                $"{RedisCommand.GEODIST}{key}{member1}{member2}{unit.ToLiteral()}",
                flags);

        /// <summary>GEOHASH: the standard geohash string of one member, or nil if it is missing.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to locate.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<string?> HashAsync(this in RespGeospatial geo, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync(
                $"{RedisCommand.GEOHASH}{key}{member}",
                flags,
                SingletonStringHandler.Instance);

        /// <summary>GEOHASH for several members at once; a missing member reads as a null element.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="members">The members to locate.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<string?>> HashAsync(this in RespGeospatial geo, RedisKey key, ReadOnlySpan<RedisValue> members, CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync(
                $"{RedisCommand.GEOHASH}{key}{members}",
                flags,
                StringLeaseHandler.Lease);

        /// <summary>GEOPOS: where one member is, or nil if it is missing.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to locate.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<GeoPosition?> PositionAsync(this in RespGeospatial geo, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync(
                $"{RedisCommand.GEOPOS}{key}{member}",
                flags,
                SingletonPositionHandler.Instance);

        /// <summary>GEOPOS for several members at once; a missing member reads as a null element.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="members">The members to locate.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<GeoPosition?>> PositionAsync(this in RespGeospatial geo, RedisKey key, ReadOnlySpan<RedisValue> members, CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync(
                $"{RedisCommand.GEOPOS}{key}{members}",
                flags,
                PositionLeaseHandler.Lease);

        /// <summary>GEOSEARCH from a member of the set.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to search around.</param>
        /// <param name="shape">The area to search.</param>
        /// <param name="count">How many results to return, or -1 for all of them.</param>
        /// <param name="demandClosest">Whether the results must be the closest ones, rather than the first found.</param>
        /// <param name="order">Which direction to sort the results in, or null to leave them unordered.</param>
        /// <param name="options">Which extra fields to ask for; they decide the reply's shape.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<GeoRadiusResult>> SearchAsync(
            this in RespGeospatial geo,
            RedisKey key,
            RedisValue member,
            GeoSearchShape shape,
            int count = -1,
            bool demandClosest = true,
            Order? order = null,
            GeoRadiusOptions options = GeoRadiusOptions.Default,
            CommandFlags flags = CommandFlags.None)
            => SearchCore(in geo, default, key, member, double.NaN, double.NaN, shape, count, demandClosest, false, order, options, flags, GeoResultHandler.Lease(options));

        /// <summary>GEOSEARCH from a coordinate.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="longitude">The longitude to search around.</param>
        /// <param name="latitude">The latitude to search around.</param>
        /// <param name="shape"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='shape']"/></param>
        /// <param name="count"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='count']"/></param>
        /// <param name="demandClosest"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='demandClosest']"/></param>
        /// <param name="order"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='order']"/></param>
        /// <param name="options"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='options']"/></param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<GeoRadiusResult>> SearchAsync(
            this in RespGeospatial geo,
            RedisKey key,
            double longitude,
            double latitude,
            GeoSearchShape shape,
            int count = -1,
            bool demandClosest = true,
            Order? order = null,
            GeoRadiusOptions options = GeoRadiusOptions.Default,
            CommandFlags flags = CommandFlags.None)
            => SearchCore(in geo, default, key, RedisValue.Null, longitude, latitude, shape, count, demandClosest, false, order, options, flags, GeoResultHandler.Lease(options));

        /// <summary>GEOSEARCHSTORE from a member: the same search, written to a key.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="destination">The key to write the matches to.</param>
        /// <param name="sourceKey">The key to read.</param>
        /// <param name="member"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='member']"/></param>
        /// <param name="shape"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='shape']"/></param>
        /// <param name="count"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='count']"/></param>
        /// <param name="demandClosest"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='demandClosest']"/></param>
        /// <param name="order"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='order']"/></param>
        /// <param name="storeDistances">Whether to store the distance as the score, rather than the position.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> SearchAndStoreAsync(
            this in RespGeospatial geo,
            RedisKey destination,
            RedisKey sourceKey,
            RedisValue member,
            GeoSearchShape shape,
            int count = -1,
            bool demandClosest = true,
            Order? order = null,
            bool storeDistances = false,
            CommandFlags flags = CommandFlags.None)
            => SearchCore(in geo, destination, sourceKey, member, double.NaN, double.NaN, shape, count, demandClosest, storeDistances, order, GeoRadiusOptions.None, flags, RespHandlers.Int64);

        /// <summary>GEOSEARCHSTORE from a coordinate.</summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="destination"><inheritdoc cref="SearchAndStoreAsync(in RespGeospatial, RedisKey, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, bool, CommandFlags)" path="/param[@name='destination']"/></param>
        /// <param name="sourceKey">The key to read.</param>
        /// <param name="longitude"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, double, double, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='longitude']"/></param>
        /// <param name="latitude"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, double, double, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='latitude']"/></param>
        /// <param name="shape"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='shape']"/></param>
        /// <param name="count"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='count']"/></param>
        /// <param name="demandClosest"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='demandClosest']"/></param>
        /// <param name="order"><inheritdoc cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)" path="/param[@name='order']"/></param>
        /// <param name="storeDistances"><inheritdoc cref="SearchAndStoreAsync(in RespGeospatial, RedisKey, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, bool, CommandFlags)" path="/param[@name='storeDistances']"/></param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> SearchAndStoreAsync(
            this in RespGeospatial geo,
            RedisKey destination,
            RedisKey sourceKey,
            double longitude,
            double latitude,
            GeoSearchShape shape,
            int count = -1,
            bool demandClosest = true,
            Order? order = null,
            bool storeDistances = false,
            CommandFlags flags = CommandFlags.None)
            => SearchCore(in geo, destination, sourceKey, RedisValue.Null, longitude, latitude, shape, count, demandClosest, storeDistances, order, GeoRadiusOptions.None, flags, RespHandlers.Int64);

        /// <summary>
        /// <see cref="HashAsync(in RespGeospatial, RedisKey, ReadOnlySpan{RedisValue}, CommandFlags)"/> for the
        /// old <see cref="IDatabase"/> shape, which promises an array the caller owns.
        /// </summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="members">The members to locate.</param>
        /// <param name="flags">Command flags.</param>
        internal static ValueTask<string?[]> HashArray(this in RespGeospatial geo, RedisKey key, ReadOnlySpan<RedisValue> members, CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync(
                $"{RedisCommand.GEOHASH}{key}{members}",
                flags,
                StringLeaseHandler.Array);

        /// <summary>
        /// <see cref="PositionAsync(in RespGeospatial, RedisKey, ReadOnlySpan{RedisValue}, CommandFlags)"/> for
        /// the old <see cref="IDatabase"/> shape, which promises an array the caller owns.
        /// </summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="members">The members to locate.</param>
        /// <param name="flags">Command flags.</param>
        internal static ValueTask<GeoPosition?[]> PositionArray(this in RespGeospatial geo, RedisKey key, ReadOnlySpan<RedisValue> members, CommandFlags flags = CommandFlags.None)
            => geo.Context.SendAsync(
                $"{RedisCommand.GEOPOS}{key}{members}",
                flags,
                PositionLeaseHandler.Array);

        /// <summary>
        /// <see cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)"/>
        /// for the old <see cref="IDatabase"/> shape, with the two origins folded into one signature.
        /// </summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to search around, or null to use the coordinates.</param>
        /// <param name="longitude">The longitude to search around, when there is no member.</param>
        /// <param name="latitude">The latitude to search around, when there is no member.</param>
        /// <param name="shape">The area to search.</param>
        /// <param name="count">How many results to return, or -1 for all of them.</param>
        /// <param name="demandClosest">Whether the results must be the closest ones.</param>
        /// <param name="order">Which direction to sort the results in.</param>
        /// <param name="options">Which extra fields to ask for.</param>
        /// <param name="flags">Command flags.</param>
        internal static ValueTask<GeoRadiusResult[]> SearchArray(
            this in RespGeospatial geo,
            RedisKey key,
            RedisValue member,
            double longitude,
            double latitude,
            GeoSearchShape shape,
            int count,
            bool demandClosest,
            Order? order,
            GeoRadiusOptions options,
            CommandFlags flags)
            => SearchCore(in geo, default, key, member, longitude, latitude, shape, count, demandClosest, false, order, options, flags, GeoResultHandler.Array(options));

        /// <summary>
        /// <c>GEOSEARCH</c> where the server has it, <c>GEORADIUS</c>/<c>GEORADIUSBYMEMBER</c> where it
        /// does not: the old <c>GeoRadius</c> shape, and nothing more.
        /// </summary>
        /// <param name="geo">The geospatial command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to search around, or null to use the coordinates.</param>
        /// <param name="longitude">The longitude to search around, when there is no member.</param>
        /// <param name="latitude">The latitude to search around, when there is no member.</param>
        /// <param name="radius">How far to search.</param>
        /// <param name="unit">The unit the radius is given in.</param>
        /// <param name="count">How many results to return, or -1 for all of them.</param>
        /// <param name="order">Which direction to sort the results in, or null to leave them unordered.</param>
        /// <param name="options">Which extra fields to ask for.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// Internal, and deliberately not on the group's own surface:
        /// <see cref="SearchAsync(in RespGeospatial, RedisKey, RedisValue, GeoSearchShape, int, bool, Order?, GeoRadiusOptions, CommandFlags)"/>
        /// is what callers want, and it takes a box as readily as a circle. This exists so that a caller
        /// of the <b>old</b> method keeps working against a server older than 6.2.
        /// </para>
        /// <para>
        /// Both deprecated commands default to a <i>write</i> retry category, because <c>STORE</c> and
        /// <c>STOREDIST</c> exist and a raw <c>Execute</c> could be using them; this typed path never
        /// emits either, so it is a pure query and says so - before the table is consulted, since the
        /// category is first-wins.
        /// </para>
        /// </remarks>
        internal static ValueTask<GeoRadiusResult[]> RadiusArray(
            this in RespGeospatial geo,
            RedisKey key,
            RedisValue member,
            double longitude,
            double latitude,
            double radius,
            GeoUnit unit,
            int count,
            Order? order,
            GeoRadiusOptions options,
            CommandFlags flags)
        {
            var context = geo.Context;
            var byMember = !member.IsNull;

            if (context.CommandMap.IsAvailable(RedisCommand.GEOSEARCH)
                && context.TryGetFeatures(RedisCommand.GEOSEARCH, in key, flags, out var features)
                && features.GeoSearch)
            {
                // GEORADIUS is exactly a circular GEOSEARCH; note the count rule differs between the two
                // old spellings and the new one, so the old one is preserved here rather than widened
                return SearchCore(
                    in geo,
                    default,
                    key,
                    member,
                    longitude,
                    latitude,
                    new GeoSearchCircle(radius, unit),
                    count > 0 ? count : -1,
                    demandClosest: true,
                    storeDistances: false,
                    order,
                    options,
                    flags,
                    GeoResultHandler.Array(options));
            }

            var command = byMember ? RedisCommand.GEORADIUSBYMEMBER : RedisCommand.GEORADIUS;
            var argCount = 1 + (byMember ? 1 : 2) + 2 + OptionCount(options)
                + (count > 0 ? 2 : 0) + (order is null ? 0 : 1);

            var cmd = context.Compose(command, argCount);
            try
            {
                cmd.AppendFormatted(key);
                if (byMember)
                {
                    cmd.AppendFormatted(member);
                }
                else
                {
                    cmd.AppendFormatted((RedisValue)longitude);
                    cmd.AppendFormatted((RedisValue)latitude);
                }

                cmd.AppendFormatted((RedisValue)radius);
                cmd.AppendFormatted(unit.ToLiteral());
                AppendOptions(ref cmd, options);

                if (count > 0)
                {
                    cmd.AppendFormatted(RespLiterals.Count);
                    cmd.AppendFormatted((RedisValue)count);
                }

                if (order is { } sort) cmd.AppendFormatted(AsFragment(sort));
            }
            catch
            {
                cmd.Dispose();
                throw;
            }

            var frame = cmd.Complete();
            return context.SendAsync(
                ref frame,
                flags.WithRetryCategory(CommandFlags.CommandRetryReadOnly),
                GeoResultHandler.Array(options),
                default);
        }

        /// <summary>The one renderer for GEOSEARCH and GEOSEARCHSTORE.</summary>
        private static ValueTask<TResult> SearchCore<TResult>(
            in RespGeospatial geo,
            in RedisKey destination,
            in RedisKey sourceKey,
            in RedisValue member,
            double longitude,
            double latitude,
            GeoSearchShape shape,
            int count,
            bool demandClosest,
            bool storeDistances,
            Order? order,
            GeoRadiusOptions options,
            CommandFlags flags,
            IRespHandler<TResult> handler)
        {
            if (shape is null) throw new ArgumentNullException(nameof(shape));
            if (!demandClosest && count < 0)
            {
                throw new ArgumentException($"{nameof(demandClosest)} must be true if you are not limiting the count for a GEOSEARCH", nameof(demandClosest));
            }

            var byMember = !member.IsNull;
            var command = destination.IsNull ? RedisCommand.GEOSEARCH : RedisCommand.GEOSEARCHSTORE;
            var argCount = (destination.IsNull ? 1 : 2) + (byMember ? 2 : 3) + shape.ArgCount
                + (order is null ? 0 : 1) + (count >= 0 ? 2 : 0) + (demandClosest ? 0 : 1)
                + OptionCount(options) + (storeDistances ? 1 : 0);

            var context = geo.Context;
            var cmd = context.Compose(command, argCount);
            try
            {
                // GEOSEARCHSTORE names the destination first; everything after it is identical
                if (!destination.IsNull) cmd.AppendFormatted(destination);
                cmd.AppendFormatted(sourceKey);

                if (byMember)
                {
                    cmd.AppendFormatted(RespLiterals.FromMember);
                    cmd.AppendFormatted(member);
                }
                else
                {
                    cmd.AppendFormatted(RespLiterals.FromLonLat);
                    cmd.AppendFormatted((RedisValue)longitude);
                    cmd.AppendFormatted((RedisValue)latitude);
                }

                shape.AddArgs(ref cmd);

                if (order is { } sort) cmd.AppendFormatted(AsFragment(sort));
                if (count >= 0)
                {
                    cmd.AppendFormatted(RespLiterals.Count);
                    cmd.AppendFormatted((RedisValue)count);
                    if (!demandClosest) cmd.AppendFormatted(RespLiterals.Any);
                }

                AppendOptions(ref cmd, options);
                if (storeDistances) cmd.AppendFormatted(RespLiterals.StoreDist);
            }
            catch
            {
                cmd.Dispose();
                throw;
            }

            var frame = cmd.Complete();
            return context.SendAsync(ref frame, flags, handler, default);
        }

        private static int OptionCount(GeoRadiusOptions options)
            => ((options & GeoRadiusOptions.WithCoordinates) == 0 ? 0 : 1)
             + ((options & GeoRadiusOptions.WithDistance) == 0 ? 0 : 1)
             + ((options & GeoRadiusOptions.WithGeoHash) == 0 ? 0 : 1);

        /// <summary>The WITH* operands, in the order the reply returns them.</summary>
        private static void AppendOptions(ref RespCommandHandler cmd, GeoRadiusOptions options)
        {
            if ((options & GeoRadiusOptions.WithCoordinates) != 0) cmd.AppendFormatted(RespLiterals.WithCoord);
            if ((options & GeoRadiusOptions.WithDistance) != 0) cmd.AppendFormatted(RespLiterals.WithDist);
            if ((options & GeoRadiusOptions.WithGeoHash) != 0) cmd.AppendFormatted(RespLiterals.WithHash);
        }

        private static RespFragment AsFragment(Order order) => order switch
        {
            Order.Ascending => RespLiterals.Asc,
            Order.Descending => RespLiterals.Desc,
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };

        /// <summary>GEOPOS for one member: an array of one, or an empty array if it is missing.</summary>
        private sealed class SingletonPositionHandler : IRespHandler<GeoPosition?>
        {
            internal static readonly SingletonPositionHandler Instance = new();

            public GeoPosition? Parse(ref RespReader reader)
            {
                if (!reader.IsAggregate || !reader.AggregateLengthIs(1)) return null;

                reader.MoveNext();
                return GeoPosition.TryRead(ref reader);
            }
        }

        /// <summary>GEOHASH for one member: an array of one, or an empty array if it is missing.</summary>
        private sealed class SingletonStringHandler : IRespHandler<string?>
        {
            internal static readonly SingletonStringHandler Instance = new();

            public string? Parse(ref RespReader reader)
            {
                if (!reader.IsAggregate || !reader.AggregateLengthIs(1)) return null;

                reader.MoveNext();
                return reader.IsNull ? null : reader.ReadString();
            }
        }

        private sealed class PositionLeaseHandler : IRespHandler<ReadOnlyLease<GeoPosition?>>, IRespHandler<GeoPosition?[]>
        {
            private static readonly PositionLeaseHandler Instance = new();

            // typed views of the one instance: it implements both interfaces explicitly, so handing the
            // instance itself to SendAsync leaves the result type ambiguous
            internal static IRespHandler<ReadOnlyLease<GeoPosition?>> Lease => Instance;

            internal static IRespHandler<GeoPosition?[]> Array => Instance;

            ReadOnlyLease<GeoPosition?> IRespHandler<ReadOnlyLease<GeoPosition?>>.Parse(ref RespReader reader)
                => RespHandlers.ReadScalarLease(ref reader, RespHandlers.Elements.Position);

            GeoPosition?[] IRespHandler<GeoPosition?[]>.Parse(ref RespReader reader)
            {
                return reader.IsNull ? [] : reader.ReadPastArray(RespHandlers.Elements.Position) ?? [];
            }
        }

        private sealed class StringLeaseHandler : IRespHandler<ReadOnlyLease<string?>>, IRespHandler<string?[]>
        {
            private static readonly StringLeaseHandler Instance = new();

            /// <inheritdoc cref="PositionLeaseHandler.Lease"/>
            internal static IRespHandler<ReadOnlyLease<string?>> Lease => Instance;

            /// <inheritdoc cref="PositionLeaseHandler.Array"/>
            internal static IRespHandler<string?[]> Array => Instance;

            ReadOnlyLease<string?> IRespHandler<ReadOnlyLease<string?>>.Parse(ref RespReader reader)
                => RespHandlers.ReadScalarLease(ref reader, RespHandlers.Elements.NullableString);

            string?[] IRespHandler<string?[]>.Parse(ref RespReader reader)
            {
                return reader.IsNull ? [] : reader.ReadPastArray(RespHandlers.Elements.NullableString) ?? [];
            }
        }

        /// <summary>
        /// The geo-query reader, which cannot be an <c>Inbuilt</c> handler: the reply's shape depends on
        /// the options the request was made with, so the handler has to know them.
        /// </summary>
        /// <remarks>
        /// Eight possible option sets, so eight cached instances - the same trick, and the same table
        /// size, as <c>ResultProcessor.GeoRadiusArray</c>. The projection is built once per instance
        /// rather than per call, which is the whole reason this is a class and not a closure.
        /// </remarks>
        private sealed class GeoResultHandler : IRespHandler<ReadOnlyLease<GeoRadiusResult>>, IRespHandler<GeoRadiusResult[]>
        {
            private static readonly GeoResultHandler?[] Instances = new GeoResultHandler?[8];

            private readonly GeoRadiusOptions _options;
            private readonly RespReader.Projection<GeoRadiusResult> _projection;

            private GeoResultHandler(GeoRadiusOptions options)
            {
                _options = options;
                _projection = (ref RespReader reader) => GeoRadiusResult.Read(ref reader, _options);
            }

            internal static IRespHandler<ReadOnlyLease<GeoRadiusResult>> Lease(GeoRadiusOptions options) => Get(options);

            internal static IRespHandler<GeoRadiusResult[]> Array(GeoRadiusOptions options) => Get(options);

            private static GeoResultHandler Get(GeoRadiusOptions options)
                => Instances[(int)options & 7] ??= new GeoResultHandler(options);

            ReadOnlyLease<GeoRadiusResult> IRespHandler<ReadOnlyLease<GeoRadiusResult>>.Parse(ref RespReader reader)
                => RespHandlers.ReadScalarLease(ref reader, _projection);

            GeoRadiusResult[] IRespHandler<GeoRadiusResult[]>.Parse(ref RespReader reader)
            {
                if (reader.IsNull) return [];

                var opts = _options;
                return reader.ReadPastArray(
                    ref opts,
                    static (ref options, ref reader) => GeoRadiusResult.Read(ref reader, options),
                    scalar: _options == GeoRadiusOptions.None) ?? [];
            }
        }
    }
}
