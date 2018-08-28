// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch.QueryBuilder
{
    /// <summary>
    /// A disjunct union node is the inverse of a UnionNode. It evaluates to true only iff <b>all</b> its
    /// children are false. Conversely, it evaluates to false if <b>any</b> of its children are true.
    /// </summary>
    /// <remarks>see DisjunctNode which evaluates to true if <b>any</b> of its children are false.</remarks>
    public class DisjunctUnionNode : DisjunctNode
    {
        protected override string GetJoinString() => "|";
    }
}
