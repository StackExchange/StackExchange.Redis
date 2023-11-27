using System;
using System.ComponentModel;

namespace StackExchange.Redis
{
    /// <summary>
    /// The underlying result type as defined by Redis.
    /// </summary>
    public enum ResultType : byte
    {
        /// <summary>
        /// No value was received.
        /// </summary>
        None = 0,

        // RESP 2

        /// <summary>
        /// Basic strings typically represent status results such as "OK".
        /// </summary>
        SimpleString = 1,
        /// <summary>
        /// Error strings represent invalid operation results from the server.
        /// </summary>
        Error = 2,
        /// <summary>
        /// Integers are returned for count operations and some integer-based increment operations.
        /// </summary>
        Integer = 3,
        /// <summary>
        /// Bulk strings represent typical user content values.
        /// </summary>
        BulkString = 4,

        /// <summary>
        /// Array of results (former Multi-bulk).
        /// </summary>
        Array = 5,

        /// <summary>
        /// Multi-bulk replies represent complex results such as arrays.
        /// </summary>
        [Obsolete("Please use " + nameof(Array))]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        MultiBulk = 5,

        // RESP3: https://github.com/redis/redis-specifications/blob/master/protocol/RESP3.md

        // note: we will arrange the values such as the last 3 bits are the RESP2 equivalent,
        // and then we count up from there

        /// <summary>
        /// A single null value replacing RESP v2 blob and multi-bulk nulls.
        /// </summary>
        Null = (1 << 3) | None,

        /// <summary>
        /// True or false.
        /// </summary>
        Boolean = (1 << 3) | Integer,

        /// <summary>
        /// A floating point number.
        /// </summary>
        Double = (1 << 3) | SimpleString,

        /// <summary>
        /// A large number non representable by the <see cref="Integer"/> type
        /// </summary>
        BigInteger = (2 << 3) | SimpleString,

        /// <summary>
        /// Binary safe error code and message.
        /// </summary>
        BlobError = (1 << 3) | Error,

        /// <summary>
        /// A binary safe string that should be displayed to humans without any escaping or filtering. For instance the output of <c>LATENCY DOCTOR</c> in Redis.
        /// </summary>
        VerbatimString = (1 << 3) | BulkString,

        /// <summary>
        /// An unordered collection of key-value pairs. Keys and values can be any other RESP3 type.
        /// </summary>
        Map = (1 << 3) | Array,

        /// <summary>
        /// An unordered collection of N other types.
        /// </summary>
        Set = (2 << 3) | Array,

        /// <summary>
        /// Like the <see cref="Map"/> type, but the client should keep reading the reply ignoring the attribute type, and return it to the client as additional information.
        /// </summary>
        Attribute = (3 << 3) | Array,

        /// <summary>
        /// Out of band data. The format is like the <see cref="Array"/> type, but the client should just check the first string element,
        /// stating the type of the out of band data, a call a callback if there is one registered for this specific type of push information.
        /// Push types are not related to replies, since they are information that the server may push at any time in the connection,
        /// so the client should keep reading if it is reading the reply of a command.
        /// </summary>
        Push = (4 << 3) | Array,
    }
}
