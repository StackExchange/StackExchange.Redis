using System;
using System.Collections.Generic;
using System.Text;

namespace NRediSearch.Aggregation
{
    public class Group
    {
        private IList<string> fields;

        public Group(IList<string> fields)
        {
            this.fields = fields;
        }

        internal void Limit(Limit limit)
        {
            throw new NotImplementedException();
        }

        internal void Reduce(Reducer r)
        {
            throw new NotImplementedException();
        }

        internal void SerializeRedisArgs(List<object> args)
        {
            throw new NotImplementedException();
        }
    }
}
