using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class TransactionWrapper : DatabaseWrapperBase<ITransaction>, ITransaction
    {
        public TransactionWrapper(ITransaction inner, RedisKey prefix)
            : base(inner, prefix)
        {
        }

        public ConditionResult AddCondition(Condition condition)
        {
            return this.Inner.AddCondition(condition == null ? null : condition.MapKeys(GetMapFunction()));
        }

        public bool Execute(CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.Execute(flags);
        }

        public Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None)
        {
            return this.Inner.ExecuteAsync(flags);
        }

        public void Execute()
        {
            this.Inner.Execute();
        }
    }
}
