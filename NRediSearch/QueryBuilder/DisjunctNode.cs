// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch.QueryBuilder
{
    /// <summary>
    /// A disjunct node. evaluates to true if any of its children are false. Conversely, this node evaluates to false
    /// only iff <b>all</b> of its children are true, making it the exact inverse of IntersectNode
    /// </summary>
    /// <remarks>DisjunctUnionNode which evalutes to true if <b>all</b> its children are false.</remarks>
    public class DisjunctNode : IntersectNode
    {
        public override string ToString(ParenMode mode)
        {
            var ret = base.ToString(ParenMode.Never);
            if (ShouldUseParens(mode))
            {
                return "-(" + ret + ")";
            }
            else
            {
                return "-" + ret;
            }
        }
    }
}
