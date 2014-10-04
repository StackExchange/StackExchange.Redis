using System;

namespace StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation
{
    internal static class ExtensionHelper
    {
        public static TInner WithKeyPrefix<TInner, TWrapper>(TInner inner, string paramName, RedisKey keyPrefix, Func<TInner, RedisKey, TWrapper> factory)
            where TInner: IRedisAsync where TWrapper : RedisWrapperBase<TInner>, TInner
        {
            if (inner == null)
            {
                throw new ArgumentNullException(paramName);
            }

            if (keyPrefix.IsNull)
            {
                throw new ArgumentNullException("keyPrefix");
            }

            if (keyPrefix.Value.Length == 0)
            {
                return inner; // fine - you can keep using the original, then
            }

            TWrapper innerAsWrapper = inner as TWrapper;
            if (innerAsWrapper != null)
            {
                // combine the key in advance to minimize indirection
                keyPrefix = innerAsWrapper.ToInner(keyPrefix);
                inner = innerAsWrapper.Inner;
            }

            return factory(inner, keyPrefix);
        }
    }
}
