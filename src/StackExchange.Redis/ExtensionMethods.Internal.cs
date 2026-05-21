using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;

namespace StackExchange.Redis
{
    internal static class ExtensionMethodsInternal
    {
        internal static bool IsNullOrEmpty([NotNullWhen(false)] this string? s) =>
            string.IsNullOrEmpty(s);

        internal static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? s) =>
            string.IsNullOrWhiteSpace(s);

        internal static RedisKey[] AssertAllNonNull(this RedisKey[] keys)
        {
            if (keys is null) throw new ArgumentNullException(nameof(keys));
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i].AssertNotNull();
            }
            return keys;
        }

        internal static RedisValue[] AssertAllNonNull(this RedisValue[] values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            for (var i = 0; i < values.Length; i++)
            {
                values[i].AssertNotNull();
            }
            return values;
        }

#if !NET
        internal static bool TryDequeue<T>(this Queue<T> queue, [NotNullWhen(true)] out T? result)
        {
            if (queue.Count == 0)
            {
                result = default;
                return false;
            }
            result = queue.Dequeue()!;
            return true;
        }
        internal static bool TryPeek<T>(this Queue<T> queue, [NotNullWhen(true)] out T? result)
        {
            if (queue.Count == 0)
            {
                result = default;
                return false;
            }
            result = queue.Peek()!;
            return true;
        }
#endif

        internal static void SetRecommendedSocketOptions(this Socket socket)
        {
            try
            {
                if (socket.AddressFamily is not AddressFamily.Unix)
                {
                    socket.NoDelay = true;
                }

                if (socket.ProtocolType is ProtocolType.Tcp)
                {
                    // enable TCP keep-alive (best effort only)
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message, nameof(Socket));
            }
        }
    }
}
