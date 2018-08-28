// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NRediSearch.QueryBuilder
{
    public abstract class QueryNode : INode
    {
        private readonly List<INode> children = new List<INode>();

        protected abstract string GetJoinString();

        /**
  * Add a match criteria to this node
  * @param field The field to check. If null or empty, then any field is checked
  * @param values Values to check for.
  * @return The current node, for chaining.
  */
        public QueryNode Add(string field, params Value[] values)
        {
            children.Add(new ValueNode(field, GetJoinString(), values));
            return this;
        }

        /**
         * Convenience method to add a list of string values
         * @param field Field to check for
         * @param values One or more string values.
         * @return The current node, for chaining.
         */
        public QueryNode Add(string field, params string[] values)
        {
            children.Add(new ValueNode(field, GetJoinString(), values));
            return this;
        }

        /**
         * Add a list of values from a collection
         * @param field The field to check
         * @param values Collection of values to match
         * @return The current node for chaining.
         */
        public QueryNode Add(string field, IList<Value> values)
        {
            return Add(field, values.ToArray());
        }

        /**
         * Add children nodes to this node.
         * @param nodes Children nodes to add
         * @return The current node, for chaining.
         */
        public QueryNode Add(params INode[] nodes)
        {
            children.AddRange(nodes);
            return this;
        }

        protected bool ShouldUseParens(ParenMode mode)
        {
            if (mode == ParenMode.Always)
            {
                return true;
            }
            else if (mode == ParenMode.Never)
            {
                return false;
            }
            else
            {
                return children.Count > 1;
            }
        }

        public virtual string ToString(ParenMode mode)
        {
            StringBuilder sb = new StringBuilder();

            if (ShouldUseParens(mode))
            {
                sb.Append("(");
            }
            var sj = new StringJoiner(sb, GetJoinString());
            foreach (var n in children)
            {
                sj.Add(n.ToString(mode));
            }
            if (ShouldUseParens(mode))
            {
                sb.Append(")");
            }
            return sb.ToString();
        }

        public override string ToString() => ToString(ParenMode.Default);
    }
}
