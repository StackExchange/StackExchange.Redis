using System;

namespace StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class BatchWrapper : DatabaseWrapperBase<IBatch>, IBatch
    {
        public BatchWrapper(IBatch inner, RedisKey prefix)
            : base(inner, prefix)
        {
        }

        public void Execute()
        {
            this.Inner.Execute();
        }
    }
}
