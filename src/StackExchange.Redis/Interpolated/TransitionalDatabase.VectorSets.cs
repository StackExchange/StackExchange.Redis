using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The vector-set commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// The thinnest adapter of the lot, because the old surface was already lease-shaped: every member is
    /// a rename plus a writable-lease sibling, and there is no array anywhere except
    /// <c>VectorSetRandomMembers</c>. The enumerating <c>VRANGE</c> stays in
    /// <c>TransitionalDatabase.Scans.cs</c> with the other cursors.
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public bool VectorSetAdd(RedisKey key, VectorSetAddRequest request, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.AddAsync(key, request, flags));

        /// <inheritdoc/>
        public Task<bool> VectorSetAddAsync(RedisKey key, VectorSetAddRequest request, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.AddAsync(key, request, flags).AsTask();

        /// <inheritdoc/>
        public long VectorSetLength(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.LengthAsync(key, flags));

        /// <inheritdoc/>
        public Task<long> VectorSetLengthAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.LengthAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public int VectorSetDimension(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.DimensionAsync(key, flags));

        /// <inheritdoc/>
        public Task<int> VectorSetDimensionAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.DimensionAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public Lease<float>? VectorSetGetApproximateVector(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.GetApproximateVectorWritableLease(key, member, flags));

        /// <inheritdoc/>
        public Task<Lease<float>?> VectorSetGetApproximateVectorAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.GetApproximateVectorWritableLease(key, member, flags).AsTask();

        /// <inheritdoc/>
        public string? VectorSetGetAttributesJson(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.GetAttributesJsonAsync(key, member, flags));

        /// <inheritdoc/>
        public Task<string?> VectorSetGetAttributesJsonAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.GetAttributesJsonAsync(key, member, flags).AsTask();

        /// <inheritdoc/>
        public VectorSetInfo? VectorSetInfo(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.InfoAsync(key, flags));

        /// <inheritdoc/>
        public Task<VectorSetInfo?> VectorSetInfoAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.InfoAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public bool VectorSetContains(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.ContainsAsync(key, member, flags));

        /// <inheritdoc/>
        public Task<bool> VectorSetContainsAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.ContainsAsync(key, member, flags).AsTask();

        /// <inheritdoc/>
        public Lease<RedisValue>? VectorSetGetLinks(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.GetLinksWritableLease(key, member, flags));

        /// <inheritdoc/>
        public Task<Lease<RedisValue>?> VectorSetGetLinksAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.GetLinksWritableLease(key, member, flags).AsTask();

        /// <inheritdoc/>
        public Lease<VectorSetLink>? VectorSetGetLinksWithScores(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.GetLinksWithScoresWritableLease(key, member, flags));

        /// <inheritdoc/>
        public Task<Lease<VectorSetLink>?> VectorSetGetLinksWithScoresAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.GetLinksWithScoresWritableLease(key, member, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue VectorSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.RandomMemberAsync(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> VectorSetRandomMemberAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.RandomMemberAsync(key, flags).AsTask();

        /// <inheritdoc/>
        public RedisValue[] VectorSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.RandomMembersArray(key, count, flags));

        /// <inheritdoc/>
        public Task<RedisValue[]> VectorSetRandomMembersAsync(RedisKey key, long count, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.RandomMembersArray(key, count, flags).AsTask();

        /// <inheritdoc/>
        public bool VectorSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.RemoveAsync(key, member, flags));

        /// <inheritdoc/>
        public Task<bool> VectorSetRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.RemoveAsync(key, member, flags).AsTask();

        /// <inheritdoc/>
        public bool VectorSetSetAttributesJson(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.SetAttributesJsonAsync(key, member, attributesJson, flags));

        /// <inheritdoc/>
        public Task<bool> VectorSetSetAttributesJsonAsync(RedisKey key, RedisValue member, string attributesJson, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.SetAttributesJsonAsync(key, member, attributesJson, flags).AsTask();

        /// <inheritdoc/>
        public Lease<VectorSetSimilaritySearchResult>? VectorSetSimilaritySearch(RedisKey key, VectorSetSimilaritySearchRequest query, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.SimilaritySearchWritableLease(key, query, flags));

        /// <inheritdoc/>
        public Task<Lease<VectorSetSimilaritySearchResult>?> VectorSetSimilaritySearchAsync(RedisKey key, VectorSetSimilaritySearchRequest query, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.SimilaritySearchWritableLease(key, query, flags).AsTask();

        /// <inheritdoc/>
        public Lease<RedisValue> VectorSetRange(RedisKey key, RedisValue start = default, RedisValue end = default, long count = -1, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Wait(Context.VectorSets.RangeWritableLease(key, start, end, count, exclude, flags))!;

        /// <inheritdoc/>
        public Task<Lease<RedisValue>?> VectorSetRangeAsync(RedisKey key, RedisValue start = default, RedisValue end = default, long count = -1, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None)
            => Context.VectorSets.RangeWritableLease(key, start, end, count, exclude, flags).AsTask();
    }
}
