using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Interpolated.Downlevel
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The command groups as <b>methods</b>, for compilers older than C# 14.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>db.Strings</c> is an extension <i>property</i>, which needs C# 14. The commands themselves are
    /// ordinary extension methods and bind anywhere, so only the group accessors are out of reach - and
    /// <c>db.Strings().GetAsync(key)</c> puts them back. Verified against real toolchains: Mono/net472 at
    /// C# 7.3, the .NET 6 SDK at C# 10 and the .NET 8 SDK at C# 12 all compile this with the property in
    /// scope at the same time, because extension-block metadata is <b>invisible</b> to a compiler that
    /// predates it, rather than merely unusable.
    /// </para>
    /// <para>
    /// <b>Do not import this on C# 14 or later.</b> There the property is visible, and having both in scope
    /// makes <c>db.Strings</c> ambiguous (<c>CS9339</c>). That is a compile error rather than a silent
    /// misbind, and it cannot misbehave at run time - both spellings construct the same value over the same
    /// context - but it is why this is a separate namespace you opt into rather than something always in
    /// scope.
    /// </para>
    /// <para>
    /// Note that a down-level consumer imports <i>both</i> namespaces: this one for the groups, and
    /// <c>StackExchange.Redis.Interpolated</c> for the commands that hang off them.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespGroups
    {
        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Arrays"/>
        public static RespArrays Arrays(this IRespKeyspaceTarget target) => new RespArrays(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Arrays"/>
        public static RespArrays Arrays(this in RespContext context) => new RespArrays(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Bitmaps"/>
        public static RespBitmaps Bitmaps(this IRespKeyspaceTarget target) => new RespBitmaps(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Bitmaps"/>
        public static RespBitmaps Bitmaps(this in RespContext context) => new RespBitmaps(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Geospatial"/>
        public static RespGeospatial Geospatial(this IRespKeyspaceTarget target) => new RespGeospatial(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Geospatial"/>
        public static RespGeospatial Geospatial(this in RespContext context) => new RespGeospatial(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Hashes"/>
        public static RespHashes Hashes(this IRespKeyspaceTarget target) => new RespHashes(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Hashes"/>
        public static RespHashes Hashes(this in RespContext context) => new RespHashes(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).HyperLogLog"/>
        public static RespHyperLogLog HyperLogLog(this IRespKeyspaceTarget target) => new RespHyperLogLog(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).HyperLogLog"/>
        public static RespHyperLogLog HyperLogLog(this in RespContext context) => new RespHyperLogLog(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Keys"/>
        public static RespKeys Keys(this IRespKeyspaceTarget target) => new RespKeys(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Keys"/>
        public static RespKeys Keys(this in RespContext context) => new RespKeys(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Lists"/>
        public static RespLists Lists(this IRespKeyspaceTarget target) => new RespLists(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Lists"/>
        public static RespLists Lists(this in RespContext context) => new RespLists(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Scripts"/>
        public static RespScripts Scripts(this IRespKeyspaceTarget target) => new RespScripts(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Scripts"/>
        public static RespScripts Scripts(this in RespContext context) => new RespScripts(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Sets"/>
        public static RespSets Sets(this IRespKeyspaceTarget target) => new RespSets(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Sets"/>
        public static RespSets Sets(this in RespContext context) => new RespSets(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).SortedSets"/>
        public static RespSortedSets SortedSets(this IRespKeyspaceTarget target) => new RespSortedSets(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).SortedSets"/>
        public static RespSortedSets SortedSets(this in RespContext context) => new RespSortedSets(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Streams"/>
        public static RespStreams Streams(this IRespKeyspaceTarget target) => new RespStreams(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Streams"/>
        public static RespStreams Streams(this in RespContext context) => new RespStreams(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Strings"/>
        public static RespStrings Strings(this IRespKeyspaceTarget target) => new RespStrings(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).Strings"/>
        public static RespStrings Strings(this in RespContext context) => new RespStrings(context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).VectorSets"/>
        public static RespVectorSets VectorSets(this IRespKeyspaceTarget target) => new RespVectorSets(target.Context);

        /// <inheritdoc cref="RespDatabaseExtensions.extension(IRespKeyspaceTarget).VectorSets"/>
        public static RespVectorSets VectorSets(this in RespContext context) => new RespVectorSets(context);
    }
}
