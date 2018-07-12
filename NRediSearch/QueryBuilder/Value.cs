// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch.QueryBuilder
{
    public abstract class Value
    {
        public virtual bool IsCombinable() => false;
    }
}
