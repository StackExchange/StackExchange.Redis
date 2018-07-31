using System.Threading.Tasks;

namespace StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class TransactionWrapper : WrapperBase<ITransaction>, ITransaction
    {
        public TransactionWrapper(ITransaction inner, byte[] prefix) : base(inner, prefix)
        {
        }

        public ConditionResult AddCondition(Condition condition)
        {
            return Inner.AddCondition(condition?.MapKeys(GetMapFunction()));
        }

        public bool Execute(CommandFlags flags = CommandFlags.None)
        {
            return Inner.Execute(flags);
        }

        public Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None)
        {
            return Inner.ExecuteAsync(flags);
        }

        public void Execute()
        {
            Inner.Execute();
        }
    }
}
