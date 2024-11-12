using System;
using System.Buffers;
using System.Collections.Concurrent;
using RESPite.Internal;
using RESPite.Messages;

namespace RESPite.Resp.Writers;

/// <summary>
/// Provides common RESP writer implementations.
/// </summary>
public static class RespWriters
{
    private static readonly Impl common = new();

    /// <summary>
    /// Writes ad-hoc payloads where the command and all arguments are <see cref="string"/>s.
    /// </summary>
    public static IWriter<ReadOnlyMemory<string>> Strings => common;

    private sealed class Impl : IWriter<ReadOnlyMemory<string>>
    {
        void IWriter<ReadOnlyMemory<string>>.Write(in ReadOnlyMemory<string> request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteArray(request.Length);
            foreach (var value in request.Span)
            {
                writer.WriteBulkString(value);
            }
            writer.Flush();
        }
    }

    /// <summary>
    /// Writes simple key-based commands.
    /// </summary>
    public sealed class KeyWriter : IWriter<ReadOnlyMemory<byte>>
    {
        private static readonly ConcurrentDictionary<string, KeyWriter> _global = new();

        /// <summary>
        /// Create a writer for the specified command.
        /// </summary>
        /// <remarks>Interned strings will share a corresponding instance.</remarks>
        public static KeyWriter Create(string command)
        {
            string? global = string.IsInterned(command);
            if (global is not null)
            {
                if (!_global.TryGetValue(command, out var existing))
                {
                    existing = new(command);
                    _global[command] = existing;
                }
                return existing;
            }

            return new(command);
        }

        private KeyWriter(string command)
        {
            _command = Constants.UTF8.GetBytes(command);
        }
        private readonly byte[] _command;

        void IWriter<ReadOnlyMemory<byte>>.Write(in ReadOnlyMemory<byte> key, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteCommand(_command, 1);
            writer.WriteBulkString(key.Span);
            writer.Flush();
        }
    }
}
