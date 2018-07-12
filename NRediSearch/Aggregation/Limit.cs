using System;
using System.Collections.Generic;

namespace NRediSearch.Aggregation
{
    internal readonly struct Limit
    {
        private readonly int _offset, _count;

        public Limit(int offset, int count)
        {
            _offset = offset;
            _count = count;
        }

        internal void SerializeRedisArgs(List<object> args)
        {
            if (_count == 0) return;
            args.Add("LIMIT".Literal());
            args.Add(_offset);
            args.Add(_count);
        }
    }
}
