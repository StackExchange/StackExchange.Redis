// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch.QueryBuilder
{
    /// <summary>
    /// The intersection node evaluates to true if any of its children are true.
    /// </summary>
    public class IntersectNode : QueryNode
    {
        protected override string GetJoinString() => " ";
    }
}
