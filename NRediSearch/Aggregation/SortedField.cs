using System;
using System.Collections.Generic;
using System.Text;
using StackExchange.Redis;

namespace NRediSearch.Aggregation
{
    public sealed class SortedField
    {
        private string field;
        private Order order;

        public SortedField(string field, Order order)
        {
            this.field = field;
            this.order = order;
        }

        internal object OrderArgValue()
        {
            throw new NotImplementedException();
        }
    }
}
