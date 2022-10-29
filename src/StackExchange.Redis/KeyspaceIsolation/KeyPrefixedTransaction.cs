using System.Threading.Tasks;

namespace StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class KeyPrefixedTransaction : KeyPrefixed<ITransaction>, ITransaction
    {
        public KeyPrefixedTransaction(ITransaction inner, byte[] prefix) : base(inner, prefix) { }

        public ConditionResult AddCondition(Condition condition) => Inner.AddCondition(condition.MapKeys(GetMapFunction()));

        public bool Execute(CommandFlags flags = CommandFlags.None) => Inner.Execute(flags);

        public Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None) => Inner.ExecuteAsync(flags);

        public void Execute() => Inner.Execute();
    }
}
