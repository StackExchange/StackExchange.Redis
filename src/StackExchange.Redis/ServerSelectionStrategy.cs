using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace StackExchange.Redis
{
    internal sealed class ServerSelectionStrategy
    {
        public const int NoSlot = -1, MultipleSlots = -2;
        private const int RedisClusterSlotCount = 16384;
        private static readonly ushort[] s_crc16tab = new ushort[]
        {
            0x0000,0x1021,0x2042,0x3063,0x4084,0x50a5,0x60c6,0x70e7,
            0x8108,0x9129,0xa14a,0xb16b,0xc18c,0xd1ad,0xe1ce,0xf1ef,
            0x1231,0x0210,0x3273,0x2252,0x52b5,0x4294,0x72f7,0x62d6,
            0x9339,0x8318,0xb37b,0xa35a,0xd3bd,0xc39c,0xf3ff,0xe3de,
            0x2462,0x3443,0x0420,0x1401,0x64e6,0x74c7,0x44a4,0x5485,
            0xa56a,0xb54b,0x8528,0x9509,0xe5ee,0xf5cf,0xc5ac,0xd58d,
            0x3653,0x2672,0x1611,0x0630,0x76d7,0x66f6,0x5695,0x46b4,
            0xb75b,0xa77a,0x9719,0x8738,0xf7df,0xe7fe,0xd79d,0xc7bc,
            0x48c4,0x58e5,0x6886,0x78a7,0x0840,0x1861,0x2802,0x3823,
            0xc9cc,0xd9ed,0xe98e,0xf9af,0x8948,0x9969,0xa90a,0xb92b,
            0x5af5,0x4ad4,0x7ab7,0x6a96,0x1a71,0x0a50,0x3a33,0x2a12,
            0xdbfd,0xcbdc,0xfbbf,0xeb9e,0x9b79,0x8b58,0xbb3b,0xab1a,
            0x6ca6,0x7c87,0x4ce4,0x5cc5,0x2c22,0x3c03,0x0c60,0x1c41,
            0xedae,0xfd8f,0xcdec,0xddcd,0xad2a,0xbd0b,0x8d68,0x9d49,
            0x7e97,0x6eb6,0x5ed5,0x4ef4,0x3e13,0x2e32,0x1e51,0x0e70,
            0xff9f,0xefbe,0xdfdd,0xcffc,0xbf1b,0xaf3a,0x9f59,0x8f78,
            0x9188,0x81a9,0xb1ca,0xa1eb,0xd10c,0xc12d,0xf14e,0xe16f,
            0x1080,0x00a1,0x30c2,0x20e3,0x5004,0x4025,0x7046,0x6067,
            0x83b9,0x9398,0xa3fb,0xb3da,0xc33d,0xd31c,0xe37f,0xf35e,
            0x02b1,0x1290,0x22f3,0x32d2,0x4235,0x5214,0x6277,0x7256,
            0xb5ea,0xa5cb,0x95a8,0x8589,0xf56e,0xe54f,0xd52c,0xc50d,
            0x34e2,0x24c3,0x14a0,0x0481,0x7466,0x6447,0x5424,0x4405,
            0xa7db,0xb7fa,0x8799,0x97b8,0xe75f,0xf77e,0xc71d,0xd73c,
            0x26d3,0x36f2,0x0691,0x16b0,0x6657,0x7676,0x4615,0x5634,
            0xd94c,0xc96d,0xf90e,0xe92f,0x99c8,0x89e9,0xb98a,0xa9ab,
            0x5844,0x4865,0x7806,0x6827,0x18c0,0x08e1,0x3882,0x28a3,
            0xcb7d,0xdb5c,0xeb3f,0xfb1e,0x8bf9,0x9bd8,0xabbb,0xbb9a,
            0x4a75,0x5a54,0x6a37,0x7a16,0x0af1,0x1ad0,0x2ab3,0x3a92,
            0xfd2e,0xed0f,0xdd6c,0xcd4d,0xbdaa,0xad8b,0x9de8,0x8dc9,
            0x7c26,0x6c07,0x5c64,0x4c45,0x3ca2,0x2c83,0x1ce0,0x0cc1,
            0xef1f,0xff3e,0xcf5d,0xdf7c,0xaf9b,0xbfba,0x8fd9,0x9ff8,
            0x6e17,0x7e36,0x4e55,0x5e74,0x2e93,0x3eb2,0x0ed1,0x1ef0
        };

        private readonly ConnectionMultiplexer multiplexer;
        private int anyStartOffset;

        private ServerEndPoint[]? map;

        public ServerSelectionStrategy(ConnectionMultiplexer multiplexer) => this.multiplexer = multiplexer;

        public ServerType ServerType { get; set; } = ServerType.Standalone;
        internal static int TotalSlots => RedisClusterSlotCount;

        /// <summary>
        /// Computes the hash-slot that would be used by the given key.
        /// </summary>
        /// <param name="key">The <see cref="RedisKey"/> to determine a slot ID for.</param>
        public int HashSlot(in RedisKey key)
        {
            if (ServerType == ServerType.Standalone || key.IsNull) return NoSlot;
            if (key.TryGetSimpleBuffer(out var arr)) // key was constructed from a byte[]
            {
                return GetClusterSlot(arr);
            }
            else
            {
                var length = key.TotalLength();
                if (length <= 256)
                {
                    Span<byte> span = stackalloc byte[length];
                    var written = key.CopyTo(span);
                    Debug.Assert(written == length, "key length/write error");
                    return GetClusterSlot(span);
                }
                else
                {
                    arr = ArrayPool<byte>.Shared.Rent(length);
                    var span = new Span<byte>(arr, 0, length);
                    var written = key.CopyTo(span);
                    Debug.Assert(written == length, "key length/write error");
                    var result = GetClusterSlot(span);
                    ArrayPool<byte>.Shared.Return(arr);
                    return result;
                }
            }
        }

        /// <summary>
        /// Computes the hash-slot that would be used by the given channel.
        /// </summary>
        /// <param name="channel">The <see cref="RedisChannel"/> to determine a slot ID for.</param>
        public int HashSlot(in RedisChannel channel)
            // note that the RedisChannel->byte[] converter is always direct, so this is not an alloc
            // (we deal with channels far less frequently, so pay the encoding cost up-front)
            => ServerType == ServerType.Standalone || channel.IsNull ? NoSlot : GetClusterSlot((byte[])channel!);

        /// <summary>
        /// Gets the hashslot for a given byte sequence.
        /// </summary>
        /// <remarks>
        /// HASH_SLOT = CRC16(key) mod 16384
        /// </remarks>
        private static unsafe int GetClusterSlot(ReadOnlySpan<byte> blob)
        {
            unchecked
            {
                fixed (byte* ptr = blob)
                {
                    fixed (ushort* crc16tab = s_crc16tab)
                    {
                        int offset = 0, count = blob.Length, start, end;
                        if ((start = IndexOf(ptr, (byte)'{', 0, count - 1)) >= 0
                            && (end = IndexOf(ptr, (byte)'}', start + 1, count)) >= 0
                            && --end != start)
                        {
                            offset = start + 1;
                            count = end - start; // note we already subtracted one via --end
                        }

                        uint crc = 0;
                        for (int i = 0; i < count; i++)
                            crc = ((crc << 8) ^ crc16tab[((crc >> 8) ^ ptr[offset++]) & 0x00FF]) & 0x0000FFFF;
                        return (int)(crc % RedisClusterSlotCount);
                    }
                }
            }
        }

        public ServerEndPoint? Select(Message message, bool allowDisconnected = false)
        {
            int slot = NoSlot;
            switch (ServerType)
            {
                case ServerType.Cluster:
                // strictly speaking some proxies use a different hashing algorithm, but the hash-tag behavior is
                // the same, so this does a pretty good job of spotting illegal commands before sending them
                case ServerType.Twemproxy:
                case ServerType.Envoyproxy:
                    slot = message.GetHashSlot(this);
                    if (slot == MultipleSlots) throw ExceptionFactory.MultiSlot(multiplexer.RawConfig.IncludeDetailInExceptions, message);
                    break;
            }
            return Select(slot, message.Command, message.Flags, allowDisconnected);
        }

        public ServerEndPoint? Select(RedisCommand command, in RedisKey key, CommandFlags flags, bool allowDisconnected = false)
        {
            int slot = ServerType == ServerType.Cluster ? HashSlot(key) : NoSlot;
            return Select(slot, command, flags, allowDisconnected);
        }

        public ServerEndPoint? Select(RedisCommand command, in RedisChannel channel, CommandFlags flags, bool allowDisconnected = false)
        {
            int slot = ServerType == ServerType.Cluster ? HashSlot(channel) : NoSlot;
            return Select(slot, command, flags, allowDisconnected);
        }

        public bool TryResend(int hashSlot, Message message, EndPoint endpoint, bool isMoved)
        {
            try
            {
                if (ServerType == ServerType.Standalone || hashSlot < 0 || hashSlot >= RedisClusterSlotCount) return false;

                ServerEndPoint server = multiplexer.GetServerEndPoint(endpoint);
                if (server != null)
                {
                    bool retry = false;
                    if ((message.Flags & CommandFlags.NoRedirect) == 0)
                    {
                        message.SetAsking(!isMoved);
                        message.SetNoRedirect(); // once is enough
                        if (isMoved) message.SetInternalCall();

                        // Note that everything so far is talking about PRIMARY nodes
                        // We might be wanting a REPLICA, so we'll check
                        ServerEndPoint? resendVia = null;
                        var command = message.Command;
                        switch (Message.GetPrimaryReplicaFlags(message.Flags))
                        {
                            case CommandFlags.DemandMaster:
                                resendVia = server.IsSelectable(command, isMoved) ? server : null;
                                break;
                            case CommandFlags.PreferMaster:
                                resendVia = server.IsSelectable(command, isMoved) ? server : FindReplica(server, command);
                                break;
                            case CommandFlags.PreferReplica:
                                resendVia = FindReplica(server, command, isMoved) ?? (server.IsSelectable(command, isMoved) ? server : null);
                                break;
                            case CommandFlags.DemandReplica:
                                resendVia = FindReplica(server, command, isMoved);
                                break;
                        }
                        if (resendVia == null)
                        {
                            multiplexer.Trace("Unable to resend to " + endpoint);
                        }
                        else
                        {
                            message.PrepareToResend(resendVia, isMoved);
#pragma warning disable CS0618 // Type or member is obsolete
                            retry = resendVia.TryWriteSync(message) == WriteResult.Success;
#pragma warning restore CS0618
                        }
                    }

                    if (isMoved) // update map; note we can still update the map even if we aren't actually going to resend
                    {
                        var arr = MapForMutation();
                        var oldServer = arr[hashSlot];
                        arr[hashSlot] = server;
                        if (oldServer != server)
                        {
                            multiplexer.OnHashSlotMoved(hashSlot, oldServer?.EndPoint, endpoint);
                        }
                    }

                    return retry;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        internal static int CombineSlot(int oldSlot, int newSlot)
        {
            if (oldSlot == MultipleSlots || newSlot == NoSlot) return oldSlot;
            if (oldSlot == NoSlot) return newSlot;
            return oldSlot == newSlot ? oldSlot : MultipleSlots;
        }

        internal int CombineSlot(int oldSlot, in RedisKey key)
        {
            if (oldSlot == MultipleSlots || key.IsNull) return oldSlot;

            int newSlot = HashSlot(key);
            if (oldSlot == NoSlot) return newSlot;
            return oldSlot == newSlot ? oldSlot : MultipleSlots;
        }

        internal int CountCoveredSlots()
        {
            var arr = map;
            if (arr == null) return 0;
            int count = 0;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null) count++;
            return count;
        }

        internal void UpdateClusterRange(int fromInclusive, int toInclusive, ServerEndPoint server)
        {
            var arr = MapForMutation();
            for (int i = fromInclusive; i <= toInclusive; i++)
            {
                arr[i] = server;
            }
        }

        private static unsafe int IndexOf(byte* ptr, byte value, int start, int end)
        {
            for (int offset = start; offset < end; offset++)
                if (ptr[offset] == value) return offset;
            return -1;
        }

        private ServerEndPoint? Any(RedisCommand command, CommandFlags flags, bool allowDisconnected) =>
            multiplexer.AnyServer(ServerType, (uint)Interlocked.Increment(ref anyStartOffset), command, flags, allowDisconnected);

        private static ServerEndPoint? FindPrimary(ServerEndPoint endpoint, RedisCommand command)
        {
            ServerEndPoint? cursor = endpoint;
            int max = 5;
            do
            {
                if (!cursor.IsReplica && cursor.IsSelectable(command)) return cursor;

                cursor = cursor.Primary;
            } while (cursor != null && --max != 0);
            return null;
        }

        private static ServerEndPoint? FindReplica(ServerEndPoint endpoint, RedisCommand command, bool allowDisconnected = false)
        {
            if (endpoint.IsReplica && endpoint.IsSelectable(command, allowDisconnected)) return endpoint;

            var replicas = endpoint.Replicas;
            var len = replicas.Length;
            uint startOffset = len <= 1 ? 0 : endpoint.NextReplicaOffset();
            for (int i = 0; i < len; i++)
            {
                endpoint = replicas[(int)(((uint)i + startOffset) % len)];
                if (endpoint.IsReplica && endpoint.IsSelectable(command, allowDisconnected)) return endpoint;
            }
            return null;
        }

        private ServerEndPoint[] MapForMutation()
        {
            var arr = map;
            if (arr == null)
            {
                lock (this)
                {
                    if (map == null) map = new ServerEndPoint[RedisClusterSlotCount];
                    arr = map;
                }
            }
            return arr;
        }

        private ServerEndPoint? Select(int slot, RedisCommand command, CommandFlags flags, bool allowDisconnected)
        {
            // Only interested in primary/replica preferences
            flags = Message.GetPrimaryReplicaFlags(flags);

            ServerEndPoint[]? arr;
            if (slot == NoSlot || (arr = map) == null) return Any(command, flags, allowDisconnected);

            ServerEndPoint endpoint = arr[slot];
            ServerEndPoint? testing;
            // but: ^^^ is the PRIMARY slots; if we want a replica, we need to do some thinking

            if (endpoint != null)
            {
                switch (flags)
                {
                    case CommandFlags.DemandReplica:
                        return FindReplica(endpoint, command) ?? Any(command, flags, allowDisconnected);
                    case CommandFlags.PreferReplica:
                        testing = FindReplica(endpoint, command);
                        if (testing is not null) return testing;
                        break;
                    case CommandFlags.DemandMaster:
                        return FindPrimary(endpoint, command) ?? Any(command, flags, allowDisconnected);
                    case CommandFlags.PreferMaster:
                        testing = FindPrimary(endpoint, command);
                        if (testing is not null) return testing;
                        break;
                }
                if (endpoint.IsSelectable(command, allowDisconnected)) return endpoint;
            }
            return Any(command, flags, allowDisconnected);
        }
    }
}
