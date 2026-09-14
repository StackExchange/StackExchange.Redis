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
    /// spelled. <see cref="Expiration"/> and <see cref="ValueCondition"/> are implemented this way -
    /// explicitly - and are what an absent optional argument looks like in practice: an
    /// <see cref="Expiration.Default"/> writes nothing, so <c>$"{cmd}{key}{value}{when}{expiry}"</c>
    /// covers the whole of SET with no branch.
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

    /// <summary>
    /// EXPERIMENTAL SPIKE. Implemented by a type that knows how to write itself as one or more RESP
    /// arguments <b>given a format specifier</b>: <c>$"{radius:km}"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> related to <see cref="IRespArgument"/> by inheritance, so a type can
    /// implement either without the other. That is not tidiness, it is the point - the three combinations
    /// are three different contracts, and the compiler enforces whichever one the type declares:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="IRespArgument"/> only - <c>$"{x}"</c> compiles, <c>$"{x:fmt}"</c> does not: the type has
    /// exactly one spelling.
    /// </description></item>
    /// <item><description>
    /// This interface only - <c>$"{x:fmt}"</c> compiles, <c>$"{x}"</c> does <b>not</b>: the format is
    /// <b>mandatory</b>. That is how a type with no safe default forces the caller to choose, which is the
    /// same trick the notes reach for when a bare <c>$"{ttl}"</c> would have to guess between EX and PX.
    /// </description></item>
    /// <item><description>
    /// Both - each spelling binds to its own arity, with no ambiguity, and the type decides what an absent
    /// format means.
    /// </description></item>
    /// </list>
    /// <para>
    /// A single interface could not express the middle case at all, and a default interface method could
    /// not be used to fake it: DIMs need runtime support this library does not have on <c>net461</c> or
    /// <c>netstandard2.0</c>.
    /// </para>
    /// <para>
    /// <b>There is deliberately no alignment counterpart.</b> RESP is length-prefixed binary, so
    /// <c>$"{key,10}"</c> would pad the payload and send a <i>different key</i> - silently. It is a
    /// compile error today (CS1739, no parameter named 'alignment') and is pinned by a test to keep it
    /// that way, because adding it "for symmetry" is the plausible mistake.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespFormattableArgument
    {
        /// <summary>Write this value as zero or more RESP arguments, in the requested format.</summary>
        /// <param name="handler">The command being written; append to it via its <c>AppendFormatted</c> methods.</param>
        /// <param name="format">
        /// The text between the <c>:</c> and the closing brace of the hole. Never null when it arrives from
        /// an interpolated string - the compiler passes the literal text, empty at worst - but declared
        /// nullable to match the shape the interpolated-string lowering looks for.
        /// </param>
        /// <remarks>See <see cref="IRespArgument.WriteTo"/> for why the parameter is <c>scoped ref</c>.</remarks>
        void WriteTo(scoped ref RespCommandHandler handler, string? format);
    }
}
