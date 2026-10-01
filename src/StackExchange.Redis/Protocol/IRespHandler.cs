using RESPite.Messages;

namespace StackExchange.Redis.Protocol
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Turns a reply into a result - the <c>ResultProcessor</c> half.
    /// </summary>
    /// <typeparam name="TResult">What parsing the reply produces.</typeparam>
    public interface IRespHandler<TResult>
    {
        /// <summary>Read a reply - cached or fresh - into a result.</summary>
        /// <param name="reader">
        /// The reply, already positioned on its first element; valid only for the duration of this call.
        /// </param>
        /// <remarks>
        /// <para>
        /// A reader rather than a span, and <b>positioned by the caller</b>: every implementation used to
        /// open with the same two lines - make a reader, <c>MoveNext</c> - so that preamble now happens once
        /// where the reply arrives, instead of once per handler.
        /// </para>
        /// <para>
        /// The point is not the saved lines. A handler that parses <i>from a reader</i> is the same thing as
        /// a row parser, so an aggregate can be built from its element handler rather than re-implemented
        /// beside it - and a reply can be parsed where it is, without first being flattened into a span.
        /// </para>
        /// <para>
        /// Do not let the reader escape: the bytes belong to a pooled buffer that may be released as soon as
        /// this returns.
        /// </para>
        /// </remarks>
        TResult Parse(ref RespReader reader);
    }
}
