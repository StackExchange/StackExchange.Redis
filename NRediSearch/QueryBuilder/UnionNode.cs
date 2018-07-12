namespace NRediSearch.QueryBuilder
{
    public class UnionNode : QueryNode
    {
        protected override string GetJoinString() => "|";
    }
}
