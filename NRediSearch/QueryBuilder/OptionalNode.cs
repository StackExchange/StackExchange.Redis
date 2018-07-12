// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch.QueryBuilder
{
    /// <summary>
    /// The optional node affects scoring and ordering. If it evaluates to true, the result is ranked
    /// higher. It is helpful to combine it with a UnionNode to rank a document higher if it meets
    /// one of several criteria.
    /// </summary>
    public class OptionalNode : IntersectNode
    {
        public override string ToString(ParenMode mode)
        {
            var ret = base.ToString(ParenMode.Never);
            if (ShouldUseParens(mode))
            {
                return "~(" + ret + ")";
            }
            else
            {
                return "~" + ret;
            }
        }
    }
}
