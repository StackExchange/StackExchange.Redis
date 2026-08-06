using System.Threading.Tasks;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class KeyPrefixedTransaction : KeyPrefixed<ITransaction>, ITransaction, IInternalTransaction
    {
        public KeyPrefixedTransaction(ITransaction inner, byte[] prefix) : base(inner, prefix)
        {
        }

        private protected override DatabaseFeatureFlags GetDatabaseFeatures()
            => base.GetDatabaseFeatures() | DatabaseFeatureFlags.Transaction;

        CommandFlags IInternalTransaction.GetAggregateRetryCategory()
            => Inner is IInternalTransaction it ? it.GetAggregateRetryCategory() : CommandFlags.CommandRetryNever;

        /// <inheritdoc/>
        public bool WasWatchConflict => Inner.WasWatchConflict;

        public ConditionResult AddCondition(Condition condition) => Inner.AddCondition(condition.MapKeys(GetMapFunction()));

        public bool Execute(CommandFlags flags = CommandFlags.None) => Inner.Execute(flags);

        public Task<bool> ExecuteAsync(CommandFlags flags = CommandFlags.None) => Inner.ExecuteAsync(flags);

        public void Execute() => Inner.Execute();
    }
}
