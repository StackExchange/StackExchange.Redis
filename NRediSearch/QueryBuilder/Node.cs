// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch.QueryBuilder
{
    public enum ParenMode
    {
        /// <summary>
        /// Always encapsulate
        /// </summary>
        Always,
        /// <summary>
        /// Never encapsulate. Note that this may be ignored if parentheses are semantically required (e.g.
        /// <pre>@foo:(val1|val2)</pre>. However something like <pre>@foo:v1 @bar:v2</pre> need not be parenthesized.
        /// </summary>
        Never,
        /// <summary>
        ///  Determine encapsulation based on number of children. If the node only has one child, it is not
        ///  parenthesized, if it has more than one child, it is parenthesized
        /// </summary>
        Default,
    }
    public interface INode
    {
        /// <summary>
        /// Returns the string form of this node.
        /// </summary>
        /// <param name="mode"> Whether the string should be encapsulated in parentheses <pre>(...)</pre></param>
        /// <returns>The string query.</returns>
        string ToString(ParenMode mode);
    }
}
