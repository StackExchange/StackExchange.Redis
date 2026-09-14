using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Implemented by a type that knows how to write itself as one or more RESP
    /// arguments, so that it can be used directly in a command hole: <c>$"{cmd}{key}{myArgument}"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <b>only</b> way the vocabulary of holes can be extended from outside this assembly.
    /// Extension <c>AppendFormatted</c> methods do not bind - the interpolated-string lowering does member
    /// lookup against instance members declared on the handler type and stops - so without this, the set of
    /// things that can appear in a hole is closed, and closed to us. See design notes section 2.2.
    /// </para>
    /// <para>
    /// <b>An implementation cannot miscount.</b> It writes by calling the handler's own
    /// <c>AppendFormatted</c> methods, which maintain the argument counters, so there is no separately
    /// declared token count to fall out of step with what was actually written - unlike
    /// <see cref="RespFragment"/>, whose <see cref="RespFragment.ArgCount"/> is an assertion the writer
    /// takes on trust.
    /// </para>
    /// <para>
    /// Writing nothing is legal and means "no argument", which is how an absent optional argument is
    /// spelled; see <see cref="RespCommandHandler.AppendFormatted(Expiration)"/> for the same idea on a
    /// built-in type.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespArgument
    {
        /// <summary>Write this value as zero or more RESP arguments.</summary>
        /// <param name="handler">The command being written; append to it via its <c>AppendFormatted</c> methods.</param>
        /// <remarks>
        /// The parameter is <c>scoped ref</c>: <c>ref</c> because the handler is a mutable
        /// <c>ref struct</c> that must not be copied, and <c>scoped</c> because that is what lets the
        /// handler pass <c>ref this</c> in without the compiler rejecting the call (CS8350/CS8352).
        /// </remarks>
        void WriteTo(scoped ref RespCommandHandler handler);
    }
}
