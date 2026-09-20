using System;
using System.Net;
using RESPite.Messages;

namespace StackExchange.Redis
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. A <c>-MOVED</c> or <c>-ASK</c> reply, read out of the frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A redirect is not an error to report, it is an instruction to follow: the slot this command wanted
    /// lives somewhere else. Reading it is therefore a separate step from turning an error reply into an
    /// exception, and has to happen <b>before</b> that, or the caller sees a failure for something the
    /// cluster considers routine.
    /// </para>
    /// <para>
    /// <b><c>MOVED</c> and <c>ASK</c> differ in what they say about the future</b>, which is why they are
    /// one type with a flag rather than two. <c>MOVED</c> means the slot has moved and the client's map
    /// is stale - follow it <i>and</i> update the map. <c>ASK</c> means this one key is mid-migration -
    /// follow it for this command only, prefixed with <c>ASKING</c>, and leave the map alone.
    /// </para>
    /// </remarks>
    internal readonly struct RespRedirect
    {
        private RespRedirect(bool isMoved, int slot, EndPoint? endpoint)
        {
            IsMoved = isMoved;
            Slot = slot;
            Endpoint = endpoint;
        }

        /// <summary>Whether this was <c>MOVED</c> (permanent) rather than <c>ASK</c> (this command only).</summary>
        public bool IsMoved { get; }

        /// <summary>The hash slot the server named.</summary>
        public int Slot { get; }

        /// <summary>Where to go, or <see langword="null"/> when the server did not say usefully.</summary>
        public EndPoint? Endpoint { get; }

        /// <summary>
        /// Whether the server redirected somewhere that cannot be dialled.
        /// </summary>
        /// <remarks>
        /// <c>?</c> is the documented placeholder for an unknown node, and an absent port parses to zero;
        /// a hostname-preferring node redirecting to a peer with no announced hostname reports exactly
        /// that. Neither can be connected to, and - importantly - neither may be read as "the node that
        /// answered". The response is a topology refresh, not a dial.
        /// </remarks>
        public bool IsUnroutable => Endpoint is null;

        /// <summary>Read a redirect out of a reply frame, if that is what it is.</summary>
        /// <param name="frame">The complete reply frame.</param>
        /// <param name="redirect">The redirect, when the frame carried one.</param>
        /// <returns>Whether this frame was a redirect at all.</returns>
        /// <remarks>
        /// Checked by prefix first, so the overwhelmingly common case - any reply that is not an error -
        /// costs a single byte comparison and no reader at all.
        /// </remarks>
        public static bool TryParse(scoped ReadOnlySpan<byte> frame, out RespRedirect redirect)
        {
            redirect = default;
            if (frame.IsEmpty || (RespPrefix)frame[0] != RespPrefix.SimpleError) return false;

            var reader = new RespReader(frame);
            if (!reader.TryMoveNext(checkError: false) || !reader.IsError) return false;
            if (!reader.TryGetSpan(out var text)) return false;

            return TryParseText(text, out redirect);
        }

        /// <summary>Read the body of an error reply, which is <c>MOVED|ASK slot endpoint</c>.</summary>
        /// <param name="text">The error text, without its prefix or terminator.</param>
        /// <param name="redirect">The redirect, when the text carried one.</param>
        internal static bool TryParseText(scoped ReadOnlySpan<byte> text, out RespRedirect redirect)
        {
            redirect = default;

            bool isMoved;
            if (StartsWith(text, "MOVED "u8)) isMoved = true;
            else if (StartsWith(text, "ASK "u8)) isMoved = false;
            else return false;

            var rest = text.Slice(isMoved ? 6 : 4);
            var space = rest.IndexOf((byte)' ');
            if (space <= 0) return false;

            if (!TryParseSlot(rest.Slice(0, space), out var slot)) return false;

            // the endpoint has to become a string eventually - EndPoint parsing is string-shaped all the
            // way down - but only on the redirect path, which is rare by construction
            var target = System.Text.Encoding.UTF8.GetString(rest.Slice(space + 1).ToArray());
            var endpoint = Format.TryParseEndPoint(target, out var parsed) && !IsUnroutableTarget(parsed)
                ? parsed
                : null;

            redirect = new RespRedirect(isMoved, slot, endpoint);
            return true;
        }

        private static bool StartsWith(scoped ReadOnlySpan<byte> value, scoped ReadOnlySpan<byte> prefix)
            => value.Length >= prefix.Length && value.Slice(0, prefix.Length).SequenceEqual(prefix);

        private static bool TryParseSlot(scoped ReadOnlySpan<byte> value, out int slot)
        {
            slot = 0;
            if (value.IsEmpty || value.Length > 5) return false;

            foreach (var digit in value)
            {
                if (digit is < (byte)'0' or > (byte)'9') return false;
                slot = (slot * 10) + (digit - '0');
            }

            return slot < ServerSelectionStrategy.TotalSlots;
        }

        /// <inheritdoc cref="IsUnroutable"/>
        private static bool IsUnroutableTarget(EndPoint endpoint) => endpoint switch
        {
            DnsEndPoint dns => dns.Port == 0 || dns.Host is "" or "?",
            IPEndPoint ip => ip.Port == 0,
            _ => false,
        };
    }
}
