using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. A command resolved once, for use as the first hole of an interpolated command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Holds a <see cref="RedisCommand"/> when the name is one this library knows, and pre-framed UTF-8
    /// bytes when it is not. The split matters, and it is not an optimisation:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// A <b>known</b> command must stay deferred, because <see cref="CommandMap"/> is per-context and may
    /// rename it or disable it - and "disabled" is signalled by the map returning nothing. Caching the
    /// bytes at construction would silently bypass both.
    /// </description></item>
    /// <item><description>
    /// An <b>unknown</b> name - a module command such as <c>FT.SEARCH</c> - can safely cache its bytes,
    /// because <see cref="CommandMap"/> cannot touch it. The map is built by walking the
    /// <c>RedisCommand</c> enum, so an override keyed on a name that does not parse is silently ignored.
    /// Nothing could rename it, so there is nothing to defer to.
    /// </description></item>
    /// </list>
    /// <para>
    /// The point of resolving once is that the parse, the UTF-8 encoding, the framing <i>and the
    /// validation</i> all happen at construction - typically a <c>static readonly</c> field - rather than
    /// per call. A malformed name fails at type-initialisation, not on the wire.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespCommand
    {
        private readonly RedisCommand _command;
        private readonly byte[]? _resp;   // pre-framed '$len\r\nNAME\r\n'; unknown + preformed
        private readonly string? _name;   // unknown + encode-per-call

        internal RespCommand(RedisCommand command)
        {
            _command = command;
            _resp = null;
            _name = null;
        }

        internal RespCommand(byte[] resp)
        {
            _command = RedisCommand.UNKNOWN;
            _resp = resp;
            _name = null;
        }

        internal RespCommand(string name)
        {
            _command = RedisCommand.UNKNOWN;
            _resp = null;
            _name = name;
        }

        /// <summary>The unresolved name, when this command encodes per call rather than being preformed.</summary>
        internal string? Name => _name;

        /// <summary>Whether this is a command this library knows, and so one the command map can affect.</summary>
        public bool IsKnown => _command != RedisCommand.UNKNOWN;

        /// <summary>The known command, or <c>UNKNOWN</c>.</summary>
        internal RedisCommand Command => _command;

        /// <summary>Whether this instance names anything at all.</summary>
        public bool IsEmpty => _command == RedisCommand.UNKNOWN && _resp is null && _name is null;

        /// <summary>Whether the RESP bytes were built once, rather than encoded on each use.</summary>
        public bool IsPreformed => _resp is not null;

        /// <summary>The RESP for this command, honouring the context's command map when it applies.</summary>
        /// <param name="context">The context being written.</param>
        internal ReadOnlySpan<byte> GetResp(in RespContext context)
        {
            if (_resp is not null) return _resp; // unknown, preformed: the map has no opinion on it
            if (_name is not null) return default; // unknown, per-call: the caller encodes it

            return context.ResolveCommand(_command);
        }

        /// <inheritdoc/>
        public override string ToString() => _name
            ?? (_resp is null ? _command.ToString() : Encoding.UTF8.GetString(_resp).Replace("\r\n", "|"));
    }

    /// <summary>EXPERIMENTAL SPIKE. Resolving a command name once.</summary>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespCommands
    {
        /// <summary>
        /// Resolve a command name, parsing and validating once.
        /// </summary>
        /// <remarks>
        /// Validation happens here rather than per call, and rejects anything that would desynchronise the
        /// connection if it reached the wire - which is the whole reason hand-built fragments are gated
        /// behind <c>SER011</c>. Here the library does the framing, so the caller cannot get it wrong; only
        /// the name is theirs, and it is checked.
        /// </remarks>
        /// <param name="name">The command name, for example <c>"GET"</c> or <c>"FT.SEARCH"</c>.</param>
        /// <param name="preform">
        /// Whether to build the RESP bytes now rather than encoding on each use. <c>false</c> - the default -
        /// suits casual, inline use; <c>true</c> suits a <c>static readonly</c> field.
        /// </param>
        /// <remarks>
        /// <para>
        /// The flag is about <b>when the encode happens</b>, and it is a real trade rather than a free win.
        /// Preforming inline would allocate a <c>byte[]</c> that is copied from once and discarded; leaving
        /// the name as a <c>string</c> lets the writer encode straight into the frame buffer, so the casual
        /// path allocates nothing extra. A field resolved once wants the opposite, and then every use is a
        /// <c>memcpy</c>.
        /// </para>
        /// <para>
        /// <b>It has no effect on a known command.</b> Those resolve through <see cref="CommandMap"/>, which
        /// already stores every mapped name as a pre-framed RESP fragment - "ready to throw directly into
        /// the stream" - so the bytes are preformed already, per map, which is the only place they *can* be
        /// preformed: the map is what decides them.
        /// </para>
        /// </remarks>
        public static RespCommand Command(this string name, bool preform = false)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A command name is required.", nameof(name));

            foreach (var c in name) Validate(c, name);

            if (RedisCommandMetadata.TryParseCI(name.AsSpan(), out var parsed) && parsed != RedisCommand.UNKNOWN)
            {
                // deferred: the context's command map may rename or disable it, and already holds the bytes
                return new RespCommand(parsed);
            }

            if (!preform) return new RespCommand(name);

            var payload = Encoding.UTF8.GetByteCount(name);
            var resp = Frame(payload, out var offset);
            Encoding.UTF8.GetBytes(name, 0, name.Length, resp, offset);
            return new RespCommand(resp);
        }

        /// <summary>
        /// Resolve a command name held as UTF-8 - typically a <c>u8</c> literal - so no <c>string</c> is
        /// involved at any point.
        /// </summary>
        /// <param name="name">The command name as UTF-8, for example <c>"FT.SEARCH"u8</c>.</param>
        /// <remarks>
        /// The lower-level entry point, for generated code and for callers who already hold bytes.
        /// <see cref="RedisCommandMetadata.TryParseCI(ReadOnlySpan{byte}, out RedisCommand)"/> matches on
        /// bytes directly, so this needs no transcoding even for known commands. Note this takes the command
        /// <b>name</b>, not framed RESP: the framing is ours to get right, which is what separates this from
        /// a hand-built <c>RespFragment</c> and its <c>SER011</c> gate.
        /// </remarks>
        public static RespCommand Command(this ReadOnlySpan<byte> name)
        {
            if (name.IsEmpty) throw new ArgumentException("A command name is required.", nameof(name));

            foreach (var b in name) Validate((char)b, null);

            if (RedisCommandMetadata.TryParseCI(name, out var parsed) && parsed != RedisCommand.UNKNOWN)
            {
                return new RespCommand(parsed);
            }

            var resp = Frame(name.Length, out var offset);
            name.CopyTo(resp.AsSpan(offset));
            return new RespCommand(resp);
        }

        private static void Validate(char c, string? name)
        {
            if (c > ' ' && c <= '~') return;

            // a CR, LF or space reaching the wire desynchronises the connection for every command that
            // follows - the SER011 hazard - so it is rejected here, once, rather than risked per call
            throw new ArgumentException(
                $"Command names must be printable ASCII without whitespace; got '{name ?? "<utf8>"}'.",
                nameof(name));
        }

        // '$len\r\n' ... '\r\n', with the payload left to the caller
        private static byte[] Frame(int payload, out int offset)
        {
            var header = Encoding.ASCII.GetBytes($"${payload}\r\n");
            var resp = new byte[header.Length + payload + 2];
            header.CopyTo(resp, 0);
            offset = header.Length;
            resp[resp.Length - 2] = (byte)'\r'; // not ^2: System.Index does not exist down-level
            resp[resp.Length - 1] = (byte)'\n';
            return resp;
        }
    }
}
