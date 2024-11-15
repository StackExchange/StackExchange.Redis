using System;
using RESPite.Resp.Commands;
using RESPite.Resp.Writers;
using static RESPite.Resp.Client.CommandFactory;

namespace RESPite.Resp.KeyValueStore;

/// <summary>
/// Server commands.
/// </summary>
public static class Server
{
    /// <summary>
    /// This is a container command for client connection commands.
    /// </summary>
    public static partial class CLIENT
    {
        /// <summary>
        /// The CLIENT SETNAME command assigns a name to the current connection.
        /// </summary>
        public static readonly RespCommand<string, Empty> SETNAME = new(Default, command: nameof(CLIENT), writer: ClientNameWriter.Instance);

        /// <summary>
        /// The CLIENT SETINFO command assigns various info attributes to the current connection which are displayed in the output of CLIENT LIST and CLIENT INFO.
        /// </summary>
        public static readonly RespCommand<(string Attribute, string Value), Empty> SETINFO = new(Default, command: nameof(CLIENT), writer: ClientInfoWriter.Instance);

        private sealed class ClientNameWriter(string command = nameof(CLIENT)) : CommandWriter<string>(command, 2)
        {
            public static ClientNameWriter Instance = new();

            protected override IRespWriter<string> Create(string command) => new ClientNameWriter(command);

            protected override void WriteArgs(in string request, ref RespWriter writer)
            {
                writer.WriteRaw("$7\r\nSETNAME\r\n"u8);
                writer.WriteBulkString(request);
            }
        }

        private sealed class ClientInfoWriter(string command = nameof(CLIENT)) : CommandWriter<(string Attribute, string Value)>(command, 3)
        {
            public static ClientInfoWriter Instance = new();

            protected override IRespWriter<(string Attribute, string Value)> Create(string command) => new ClientInfoWriter(command);

            protected override void WriteArgs(in (string Attribute, string Value) request, ref RespWriter writer)
            {
                writer.WriteRaw("$7\r\nSETINFO\r\n"u8);
                writer.WriteBulkString(request.Attribute);
                writer.WriteBulkString(request.Value);
            }
        }
    }

    /// <summary>
    /// This is a container command for runtime configuration commands.
    /// </summary>
    public static class CONFIG
    {
        /// <summary>
        /// The CONFIG GET command is used to read the configuration parameters of a running Redis server. Not all the configuration parameters are supported in Redis 2.4, while Redis 2.6 can read the whole configuration of a server using this command.
        /// </summary>
        public static readonly RespCommand<string, LeasedString> GET = new(Default, command: nameof(CONFIG), writer: ConfigGetWriter.Instance);

        private sealed class ConfigGetWriter(string command = nameof(CONFIG)) : CommandWriter<string>(command, 2)
        {
            public static ConfigGetWriter Instance = new();

            protected override IRespWriter<string> Create(string command) => new ConfigGetWriter(command);

            protected override void WriteArgs(in string request, ref RespWriter writer)
            {
                writer.WriteRaw("$3\r\nGET\r\n"u8);
                writer.WriteBulkString(request);
            }
        }
    }

    /// <summary>
    /// The INFO command returns information and statistics about the server in a format that is simple to parse by computers and easy to read by humans.
    /// </summary>
    public static readonly RespCommand<string, LeasedString> INFO = new(Default);
}
