// .NET port of https://github.com/RedisLabs/JRediSearch/

namespace NRediSearch.QueryBuilder
{
    /// <summary>
    /// <para>
    /// This class contains methods to construct query nodes. These query nodes can be added to parent query
    /// nodes (building a chain) or used as the root query node.
    /// </para>
    /// <para>You can use <pre>using static</pre> for these helper methods.</para>
    /// </summary>
    public static class QueryBuilder
    {
        public static QueryNode Intersect() => new IntersectNode();

        /// <summary>
        /// Create a new intersection node with child nodes. An intersection node is true if all its children
        /// are also true
        /// </summary>
        /// <param name="n">sub-condition to add</param>
        /// <returns>The node</returns>
        public static QueryNode Intersect(params INode[] n) => Intersect().Add(n);

        /// <summary>
        /// Create a new intersection node with a field-value pair.
        /// </summary>
        /// <param name="field">The field that should contain this value. If this value is empty, then any field will be checked.</param>
        /// <param name="values">Value to check for. The node will be true only if the field (or any field) contains *all* of the values.</param>
        /// <returns>The query node.</returns>
        public static QueryNode Intersect(string field, params Value[] values) => Intersect().Add(field, values);

        /// <summary>
        /// Helper method to create a new intersection node with a string value.
        /// </summary>
        /// <param name="field">The field to check. If left null or empty, all fields will be checked.</param>
        /// <param name="stringValue">The value to check.</param>
        /// <returns>The query node.</returns>
        public static QueryNode Intersect(string field, string stringValue) => Intersect(field, Values.Value(stringValue));

        public static QueryNode Union() => new UnionNode();

        /// <summary>
        /// Create a union node. Union nodes evaluate to true if <i>any</i> of its children are true.
        /// </summary>
        /// <param name="n">Child node.</param>
        /// <returns>The union node.</returns>
        public static QueryNode Union(params INode[] n) => Union().Add(n);

        /// <summary>
        /// Create a union node which can match an one or more values.
        /// </summary>
        /// <param name="field">Field to check. If empty, all fields are checked.</param>
        /// <param name="values">Values to search for. The node evaluates to true if <paramref name="field"/> matches any of the values.</param>
        /// <returns>The union node.</returns>
        public static QueryNode Union(string field, params Value[] values) => Union().Add(field, values);

        /// <summary>
        /// Convenience method to match one or more strings. This is equivalent to <see cref="Union(string, Value[])"/>.
        /// </summary>
        /// <param name="field">Field to match.</param>
        /// <param name="values">Strings to check for.</param>
        /// <returns>The union node.</returns>
        public static QueryNode Union(string field, params string[] values) => Union(field, Values.Value(values));

        public static QueryNode Disjunct() => new DisjunctNode();

        /// <summary>
        /// Create a disjunct node. Disjunct nodes are true iff <b>any</b> of its children are <b>not</b> true.
        /// Conversely, this node evaluates to false if <b>all</b> its children are true.
        /// </summary>
        /// <param name="n">Child nodes to add.</param>
        /// <returns>The disjunct node.</returns>
        public static QueryNode Disjunct(params INode[] n) => Disjunct().Add(n);

        /// <summary>
        /// Create a disjunct node using one or more values. The node will evaluate to true iff the field does not
        /// match <b>any</b> of the values.
        /// </summary>
        /// <param name="field">Field to check for (empty or null for any field).</param>
        /// <param name="values">The values to check for.</param>
        /// <returns>The disjunct node.</returns>
        public static QueryNode Disjunct(string field, params Value[] values) => Disjunct().Add(field, values);

        /// <summary>
        /// Create a disjunct node using one or more values. The node will evaluate to true iff the field does not
        /// match <b>any</b> of the values.
        /// </summary>
        /// <param name="field">Field to check for (empty or null for any field).</param>
        /// <param name="values">The values to check for.</param>
        /// <returns>The disjunct node.</returns>
        public static QueryNode Disjunct(string field, params string[] values) => Disjunct(field, Values.Value(values));

        public static QueryNode DisjunctUnion() => new DisjunctUnionNode();

        /// <summary>
        /// Create a disjunct union node. This node evaluates to true if <b>all</b> of its children are not true.
        /// Conversely, this node evaluates as false if <b>any</b> of its children are true.
        /// </summary>
        /// <param name="n">The nodes to union.</param>
        /// <returns>The node.</returns>
        public static QueryNode DisjunctUnion(params INode[] n) => DisjunctUnion().Add(n);

        public static QueryNode DisjunctUnion(string field, params Value[] values) => DisjunctUnion().Add(field, values);

        public static QueryNode DisjunctUnion(string field, params string[] values) => DisjunctUnion(field, Values.Value(values));

        /// <summary>
        /// Creates a new <see cref="OptionalNode"/>.
        /// </summary>
        /// <returns>The new <see cref="OptionalNode"/>.</returns>
        public static QueryNode Optional() => new OptionalNode();

        /// <summary>
        /// Create an optional node. Optional nodes do not affect which results are returned but they influence
        /// ordering and scoring.
        /// </summary>
        /// <param name="n">The nodes to evaluate as optional.</param>
        /// <returns>The new node.</returns>
        public static QueryNode Optional(params INode[] n) => Optional().Add(n);

        public static QueryNode Optional(string field, params Value[] values) => Optional().Add(field, values);
    }
}
