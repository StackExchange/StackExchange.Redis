using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Downlevel
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
    public static class RespGroups
    {
        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Arrays"/>
        public static RespArrays Arrays<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespArrays(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Arrays"/>
        public static RespArrays Arrays(this in RespDatabaseContext context) => new RespArrays(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Bitmaps"/>
        public static RespBitmaps Bitmaps<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespBitmaps(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Bitmaps"/>
        public static RespBitmaps Bitmaps(this in RespDatabaseContext context) => new RespBitmaps(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Geospatial"/>
        public static RespGeospatial Geospatial<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespGeospatial(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Geospatial"/>
        public static RespGeospatial Geospatial(this in RespDatabaseContext context) => new RespGeospatial(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Hashes"/>
        public static RespHashes Hashes<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespHashes(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Hashes"/>
        public static RespHashes Hashes(this in RespDatabaseContext context) => new RespHashes(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).HyperLogLog"/>
        public static RespHyperLogLog HyperLogLog<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespHyperLogLog(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).HyperLogLog"/>
        public static RespHyperLogLog HyperLogLog(this in RespDatabaseContext context) => new RespHyperLogLog(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Keys"/>
        public static RespKeys Keys<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespKeys(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Keys"/>
        public static RespKeys Keys(this in RespDatabaseContext context) => new RespKeys(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Lists"/>
        public static RespLists Lists<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespLists(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Lists"/>
        public static RespLists Lists(this in RespDatabaseContext context) => new RespLists(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Scripts"/>
        public static RespScripts Scripts<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespScripts(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Scripts"/>
        public static RespScripts Scripts(this in RespDatabaseContext context) => new RespScripts(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Sets"/>
        public static RespSets Sets<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespSets(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Sets"/>
        public static RespSets Sets(this in RespDatabaseContext context) => new RespSets(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).SortedSets"/>
        public static RespSortedSets SortedSets<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespSortedSets(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).SortedSets"/>
        public static RespSortedSets SortedSets(this in RespDatabaseContext context) => new RespSortedSets(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Streams"/>
        public static RespStreams Streams<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespStreams(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Streams"/>
        public static RespStreams Streams(this in RespDatabaseContext context) => new RespStreams(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Strings"/>
        public static RespStrings Strings<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespStrings(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).Strings"/>
        public static RespStrings Strings(this in RespDatabaseContext context) => new RespStrings(context.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).VectorSets"/>
        public static RespVectorSets VectorSets<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget => new RespVectorSets(target.Raw);

        /// <inheritdoc cref="RespDatabaseExtensions.extension{TTarget}(TTarget).VectorSets"/>
        public static RespVectorSets VectorSets(this in RespDatabaseContext context) => new RespVectorSets(context.Raw);
    }
}
