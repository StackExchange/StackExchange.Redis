using StackExchange.Redis;
using System.Collections;
namespace NRediSearch
{
    // encode and box literals once only
    internal sealed class LiteralCache
    {
        private static Hashtable _boxed = new Hashtable();

        public object this[string value]
        {
            get
            {
                if (value == null) return "";
                object boxed = _boxed[value];
                if (boxed == null)
                {
                    lock (_boxed)
                    {
                        boxed = _boxed[value];
                        if (boxed == null)
                        {
                            boxed = (RedisValue)value;
                            _boxed.Add(value, boxed);
                        }
                    }
                }
                return boxed;
            }
        }
    }
}
