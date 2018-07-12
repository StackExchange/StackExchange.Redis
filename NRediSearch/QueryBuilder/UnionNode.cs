// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch.QueryBuilder
{
    public class UnionNode : QueryNode
    {
        protected override string GetJoinString() => "|";
    }
}
