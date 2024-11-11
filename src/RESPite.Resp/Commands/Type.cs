using System;
using System.Buffers;
using RESPite.Messages;

namespace RESPite.Resp.Commands;

/// <summary>
/// Queries the type of data in a RESP database.
/// </summary>
public readonly struct Type : IRespCommand<Type, Type.KnownType>
{
    /// <summary>
    /// Database storage type.
    /// </summary>
    public enum KnownType : byte
    {
        /// <summary>
        /// An unknown or unrecognized value.
        /// </summary>
        Unknown,

        /// <summary>
        /// Strings.
        /// </summary>
        String,

        /// <summary>
        /// Lists.
        /// </summary>
        List,

        /// <summary>
        /// Sets.
        /// </summary>
        Set,

        /// <summary>
        /// Sorted sets.
        /// </summary>
        ZSet,

        /// <summary>
        /// Hashes (maps).
        /// </summary>
        Hash,

        /// <summary>
        /// Streams.
        /// </summary>
        Stream,
    }

    private readonly string? _s;
    private readonly ReadOnlyMemory<byte> _rom;

    IWriter<Type> IRespCommand<Type, KnownType>.Writer => Handler.Instance;

    IReader<Empty, KnownType> IRespCommand<Type, KnownType>.Reader => Handler.Instance;

    /// <summary>
    /// Create a new TYPE command.
    /// </summary>
    public Type(ReadOnlyMemory<byte> key)
    {
        _s = null;
        _rom = key;
    }

    /// <summary>
    /// Create a new TYPE command.
    /// </summary>
    public Type(string key)
    {
        _s = key;
        _rom = default;
    }

    private sealed class Handler : IWriter<Type>, IReader<Empty, KnownType>
    {
        private Handler() { }
        public static readonly Handler Instance = new Handler();

        KnownType IReader<Empty, KnownType>.Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            RespReader reader = new(content);
            reader.ReadNextScalar();
            var result = reader.ReadEnum<KnownType>(KnownType.Unknown);
            reader.ReadEnd();
            return result;
        }

        void IWriter<Type>.Write(in Type request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw("*2\r\n$4\r\nTYPE\r\n"u8);
            if (request._s is null)
            {
                writer.WriteBulkString(request._rom.Span);
            }
            else
            {
                writer.WriteBulkString(request._s);
            }
        }
    }
}
