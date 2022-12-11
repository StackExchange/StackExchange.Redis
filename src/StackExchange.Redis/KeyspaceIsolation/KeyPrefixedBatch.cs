namespace StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class KeyPrefixedBatch : KeyPrefixed<IBatch>, IBatch
    {
        public KeyPrefixedBatch(IBatch inner, byte[] prefix) : base(inner, prefix) { }

        public void Execute() => Inner.Execute();
    }
}
