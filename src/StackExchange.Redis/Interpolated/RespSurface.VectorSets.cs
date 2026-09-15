using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The vector-set group: <c>target.VectorSets.Add(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one group that was already the right shape: the old surface hands back <c>Lease&lt;float&gt;</c>
    /// and <c>Lease&lt;VectorSetLink&gt;</c> rather than arrays, because vector sets arrived after that
    /// argument was settled. So this is the rare move with no shape to decide - only
    /// <see cref="ReadOnlyLease{T}"/> in place of <see cref="Lease{T}"/> on the public side, with the
    /// writable siblings kept internal for <see cref="IDatabase"/>.
    /// </para>
    /// <para>
    /// <c>VRANGE</c>'s enumerating form stays with the scans: it issues a command per batch, and deferred
    /// execution is not a frame.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespVectorSets
    {
        private readonly RespContext _context;

        /// <summary>Group the vector-set commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespVectorSets(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The vector-set commands.</summary>
            public RespVectorSets VectorSets => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The vector-set commands.</summary>
            public RespVectorSets VectorSets => new(context);
        }

        /// <summary>VCARD: how many members the set holds.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> Length(this in RespVectorSets sets, RedisKey key, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<long>(
                $"{RedisCommand.VCARD}{key}", flags.WithDefaultCategory(RedisCommand.VCARD));

        /// <summary>VDIM: how many components each vector has.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<int> Dimension(this in RespVectorSets sets, RedisKey key, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync(
                $"{RedisCommand.VDIM}{key}", flags.WithDefaultCategory(RedisCommand.VDIM), Int32Handler.Instance);

        /// <summary>VISMEMBER: whether the set holds this member.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to look for.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Contains(this in RespVectorSets sets, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.VISMEMBER}{key}{member}", flags.WithDefaultCategory(RedisCommand.VISMEMBER));

        /// <summary>VREM: drop a member.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="member">The member to remove.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> Remove(this in RespVectorSets sets, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.VREM}{key}{member}", flags.WithDefaultCategory(RedisCommand.VREM));

        /// <summary>VRANDMEMBER: one member at random, or nil if the set is empty.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> RandomMember(this in RespVectorSets sets, RedisKey key, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<RedisValue>(
                $"{RedisCommand.VRANDMEMBER}{key}", flags.WithDefaultCategory(RedisCommand.VRANDMEMBER));

        /// <summary>VRANDMEMBER with a count; negative allows repeats.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="count">How many to take; negative to allow the same member more than once.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<RedisValue>> RandomMembers(this in RespVectorSets sets, RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<ReadOnlyLease<RedisValue>>(
                $"{RedisCommand.VRANDMEMBER}{key}{count}", flags.WithDefaultCategory(RedisCommand.VRANDMEMBER));

        /// <summary>VGETATTR: the JSON attributes attached to a member, or nil if it has none.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<string?> GetAttributesJson(this in RespVectorSets sets, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<string?>(
                $"{RedisCommand.VGETATTR}{key}{member}", flags.WithDefaultCategory(RedisCommand.VGETATTR));

        /// <summary>VSETATTR: attach JSON attributes to a member.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="member">The member to annotate.</param>
        /// <param name="attributesJson">The attributes, as JSON.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> SetAttributesJson(
            this in RespVectorSets sets,
            RedisKey key,
            RedisValue member,
#if NET8_0_OR_GREATER
            [StringSyntax(StringSyntaxAttribute.Json)]
#endif
            string attributesJson,
            CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync<bool>(
                $"{RedisCommand.VSETATTR}{key}{member}{attributesJson.AsRedisValue()}",
                flags.WithDefaultCategory(RedisCommand.VSETATTR));

        /// <summary>VEMB: the stored vector for a member, as the server approximates it.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to read.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// Approximate because quantization is lossy: what comes back is what the index holds, not what
        /// was written, unless the set was built with <see cref="VectorSetQuantization.None"/>.
        /// </remarks>
        public static ValueTask<ReadOnlyLease<float>?> GetApproximateVector(this in RespVectorSets sets, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync(
                $"{RedisCommand.VEMB}{key}{member}", flags.WithDefaultCategory(RedisCommand.VEMB), Float32Handler.Lease);

        /// <summary>VLINKS: the neighbours of a member, flattened across the index's layers.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<RedisValue>?> GetLinks(this in RespVectorSets sets, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync(
                $"{RedisCommand.VLINKS}{key}{member}", flags.WithDefaultCategory(RedisCommand.VLINKS), LinkHandler.MembersLease);

        /// <summary>VLINKS WITHSCORES: the same neighbours, with their distances.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="member">The member to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<VectorSetLink>?> GetLinksWithScores(this in RespVectorSets sets, RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync(
                $"{RedisCommand.VLINKS}{key}{member}{RespLiterals.WithScores}",
                flags.WithDefaultCategory(RedisCommand.VLINKS),
                LinkHandler.ScoredLease);

        /// <summary>VINFO: what the index says about itself.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<VectorSetInfo?> Info(this in RespVectorSets sets, RedisKey key, CommandFlags flags = CommandFlags.None)
            => sets.Context.SendAsync(
                $"{RedisCommand.VINFO}{key}", flags.WithDefaultCategory(RedisCommand.VINFO), InfoHandler.Instance);

        /// <summary>VRANGE: members in lexicographic order, between two bounds.</summary>
        /// <param name="sets">The vector-set command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="start">The lower bound, or default for "from the beginning".</param>
        /// <param name="end">The upper bound, or default for "to the end".</param>
        /// <param name="count">How many to return, or -1 for all of them.</param>
        /// <param name="exclude">Which bounds to treat as exclusive.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// Deliberately not called a scan: the server has no <c>VSCAN</c>, and if one arrives it should be
        /// free to take the name.
        /// </remarks>
        public static ValueTask<ReadOnlyLease<RedisValue>> Range(
            this in RespVectorSets sets,
            RedisKey key,
            RedisValue start = default,
            RedisValue end = default,
            long count = -1,
            Exclude exclude = Exclude.None,
            CommandFlags flags = CommandFlags.None)
        {
            // the bound spelling is shared with the MessageWriter path; see RedisDatabase.VectorSetBound
            var from = RedisDatabase.VectorSetBound(start, exclude, isStart: true);
            var to = RedisDatabase.VectorSetBound(end, exclude, isStart: false);

            return count < 0
                ? sets.Context.SendAsync<ReadOnlyLease<RedisValue>>(
                    $"{RedisCommand.VRANGE}{key}{from}{to}", flags.WithDefaultCategory(RedisCommand.VRANGE))
                : sets.Context.SendAsync<ReadOnlyLease<RedisValue>>(
                    $"{RedisCommand.VRANGE}{key}{from}{to}{count}", flags.WithDefaultCategory(RedisCommand.VRANGE));
        }

        /// <summary>VDIM replies with a count that is an <see cref="int"/> on the old surface.</summary>
        private sealed class Int32Handler : IRespHandler<int>
        {
            internal static readonly Int32Handler Instance = new();

            public int Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return checked((int)reader.ReadInt64());
            }
        }

        /// <summary>VEMB: a flat array of components, which the server sends as text.</summary>
        private sealed class Float32Handler : IRespHandler<ReadOnlyLease<float>?>, IRespHandler<Lease<float>?>
        {
            private static readonly Float32Handler Instance = new();

            internal static IRespHandler<ReadOnlyLease<float>?> Lease => Instance;

            internal static IRespHandler<Lease<float>?> Writable => Instance;

            ReadOnlyLease<float>? IRespHandler<ReadOnlyLease<float>?>.Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.IsNull ? null : RespHandlers.ReadScalarLease<float>(response, static (ref r) => (float)r.ReadDouble());
            }

            Lease<float>? IRespHandler<Lease<float>?>.Parse(ReadOnlySpan<byte> response)
                => CopyOut(((IRespHandler<ReadOnlyLease<float>?>)Instance).Parse(response));
        }

        /// <summary>
        /// VLINKS, in both spellings: an array per index layer, flattened into one run.
        /// </summary>
        /// <remarks>
        /// The layers are an implementation detail of the HNSW index rather than something a caller asked
        /// about, so the old surface flattens them and this does too - the alternative is a lease of
        /// leases, which nobody wants to dispose.
        /// </remarks>
        private sealed class LinkHandler :
            IRespHandler<ReadOnlyLease<RedisValue>?>,
            IRespHandler<Lease<RedisValue>?>,
            IRespHandler<ReadOnlyLease<VectorSetLink>?>,
            IRespHandler<Lease<VectorSetLink>?>
        {
            private static readonly LinkHandler Instance = new();

            internal static IRespHandler<ReadOnlyLease<RedisValue>?> MembersLease => Instance;

            internal static IRespHandler<Lease<RedisValue>?> MembersWritable => Instance;

            internal static IRespHandler<ReadOnlyLease<VectorSetLink>?> ScoredLease => Instance;

            internal static IRespHandler<Lease<VectorSetLink>?> ScoredWritable => Instance;

            ReadOnlyLease<RedisValue>? IRespHandler<ReadOnlyLease<RedisValue>?>.Parse(ReadOnlySpan<byte> response)
                => ReadFlattened(response, 1, static (ref RespReader r) => r.ReadRedisValue());

            Lease<RedisValue>? IRespHandler<Lease<RedisValue>?>.Parse(ReadOnlySpan<byte> response)
                => CopyOut(ReadFlattened(response, 1, static (ref RespReader r) => r.ReadRedisValue()));

            ReadOnlyLease<VectorSetLink>? IRespHandler<ReadOnlyLease<VectorSetLink>?>.Parse(ReadOnlySpan<byte> response)
                => ReadFlattened(response, 2, static (ref RespReader r) => VectorSetLink.Read(ref r));

            Lease<VectorSetLink>? IRespHandler<Lease<VectorSetLink>?>.Parse(ReadOnlySpan<byte> response)
                => CopyOut(ReadFlattened(response, 2, static (ref RespReader r) => VectorSetLink.Read(ref r)));
        }

        /// <summary>VINFO: an attribute map, read through the type that carries the field names.</summary>
        private sealed class InfoHandler : IRespHandler<VectorSetInfo?>
        {
            internal static readonly InfoHandler Instance = new();

            public VectorSetInfo? Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return VectorSetInfo.TryRead(ref reader, out var info) ? info : null;
            }
        }

        /// <summary>
        /// Read a reply of the form <c>[[a],[b,c]]</c> as one run of <paramref name="tokensPerElement"/>
        /// tokens each.
        /// </summary>
        private static ReadOnlyLease<T>? ReadFlattened<T>(ReadOnlySpan<byte> response, int tokensPerElement, RespReader.Projection<T> projection)
        {
            var reader = new RespReader(response);
            reader.MoveNext();
            if (!reader.IsAggregate || reader.IsNull) return null;

            // two passes, as the old processor does: the total is not known until every layer has been
            // counted, and a lease wants its length up front
            long total = 0;
            var iter = reader.AggregateChildren();
            while (iter.MoveNext())
            {
                if (iter.Value.IsAggregate && !iter.Value.IsNull)
                {
                    total += iter.Value.AggregateLength() / tokensPerElement;
                }
            }

            if (total == 0) return ReadOnlyLease<T>.Empty;

            var lease = ReadOnlyLease<T>.Rent(checked((int)total), null, out var target);
            try
            {
                var index = 0;
                iter = reader.AggregateChildren();
                while (iter.MoveNext())
                {
                    if (!iter.Value.IsAggregate || iter.Value.IsNull) continue;

                    // the LAYER's reader, not one scoped to a single element: a scored link is two
                    // successive tokens, so the projection has to be able to reach its partner
                    var layer = iter.Value;
                    while (index < target.Length && layer.TryMoveNext())
                    {
                        target[index++] = projection(ref layer);
                    }
                }

                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        /// <summary>
        /// The writable sibling of a read-only lease: <see cref="IDatabase"/> hands out
        /// <see cref="Lease{T}"/>, which the caller may write to, so it cannot share storage.
        /// </summary>
        private static Lease<T>? CopyOut<T>(ReadOnlyLease<T>? source)
        {
            if (source is null) return null;
            using (source)
            {
                if (source.IsEmpty) return Lease<T>.Empty;

                var lease = Lease<T>.Create(source.Length, clear: false);
                source.Span.CopyTo(lease.Span);
                return lease;
            }
        }
    }
}
