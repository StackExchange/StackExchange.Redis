using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class KeyPrefixedBatch : KeyPrefixed<IBatch>, IBatch
    {
        public KeyPrefixedBatch(IBatch inner, byte[] prefix) : base(inner, prefix)
        {
            inner.RejectFlags(DatabaseFeatureFlags.Batch | DatabaseFeatureFlags.Transaction);
        }

        private protected override DatabaseFeatureFlags GetDatabaseFeatures()
            => base.GetDatabaseFeatures() | DatabaseFeatureFlags.Batch;

        public void Execute() => Inner.Execute();
    }
}
