using System;
using System.Buffers;
using System.Diagnostics;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Server
{
    public readonly ref struct RedisRequest
    {
        private readonly RespReader _rootReader;

        public int Count { get; }

        public override string ToString() => Count == 0 ? "(n/a)" : GetString(0);
        public override bool Equals(object obj) => throw new NotSupportedException();

        public TypedRedisValue WrongArgCount() => TypedRedisValue.Error($"ERR wrong number of arguments for '{ToString()}' command");

        public TypedRedisValue CommandNotFound()
            => TypedRedisValue.Error($"ERR unknown command '{ToString()}'");

        public TypedRedisValue UnknownSubcommandOrArgumentCount() => TypedRedisValue.Error($"ERR Unknown subcommand or wrong number of arguments for '{ToString()}'.");

        public string GetString(int index) => GetReader(index).ReadString();

        [Obsolete("Use IsString(int, ReadOnlySpan{byte}) instead.")]
        public bool IsString(int index, string value)
            => GetReader(index).Is(value);

        public bool IsString(int index, ReadOnlySpan<byte> value)
            => GetReader(index).Is(value);

        public override int GetHashCode() => throw new NotSupportedException();

        /// <summary>
        /// Get a reader initialized at the start of the payload.
        /// </summary>
        public RespReader GetReader() => _rootReader;

        /// <summary>
        /// Get a reader initialized at the start of the payload.
        /// </summary>
        private RespReader GetReader(int childIndex)
        {
            if (childIndex < 0 || childIndex >= Count) Throw();
            var reader = GetReader();
            reader.MoveNextAggregate();
            for (int i = 0; i < childIndex; i++)
            {
                reader.MoveNextScalar();
            }
            reader.MoveNextScalar();
            return reader;

            static void Throw() => throw new ArgumentOutOfRangeException(nameof(childIndex));
        }

        internal RedisRequest(scoped in RespReader reader, ref byte[] commandLease)
        {
            _rootReader = reader;
            var local = reader;
            if (local.TryMoveNext(checkError: false) & local.IsAggregate)
            {
                Count = local.AggregateLength();
            }

            if (Count == 0)
            {
                Command = s_EmptyCommand;
            }
            else
            {
                local.MoveNextScalar();
                var len = local.ScalarLength();
                if (len > commandLease.Length)
                {
                    ArrayPool<byte>.Shared.Return(commandLease);
                    commandLease = ArrayPool<byte>.Shared.Rent(len);
                }
                var readBytes = local.CopyTo(commandLease);
                Debug.Assert(readBytes == len);
                AsciiHash.ToLower(commandLease.AsSpan(0, readBytes));
                // note we retain the lease array in the Command, this is intentional
                Command = new(commandLease, 0, readBytes);
            }
        }

        internal static byte[] GetLease() => ArrayPool<byte>.Shared.Rent(16);
        internal static void ReleaseLease(ref byte[] commandLease)
        {
            ArrayPool<byte>.Shared.Return(commandLease);
            commandLease = [];
        }

        private static readonly AsciiHash s_EmptyCommand = new(Array.Empty<byte>());
        public readonly AsciiHash Command;

        internal RedisRequest(ReadOnlySpan<byte> payload, ref byte[] commandLease) : this(new RespReader(payload), ref commandLease) { }
        internal RedisRequest(in ReadOnlySequence<byte> payload, ref byte[] commandLease) : this(new RespReader(payload), ref commandLease) { }

        public RedisValue GetValue(int index) => GetReader(index).ReadRedisValue();

        public int GetInt32(int index) => GetReader(index).ReadInt32();

        public long GetInt64(int index) => GetReader(index).ReadInt64();

        public RedisKey GetKey(int index) => GetReader(index).ReadRedisKey();

        internal RedisChannel GetChannel(int index, RedisChannel.RedisChannelOptions options)
            => throw new NotImplementedException();
    }
}
