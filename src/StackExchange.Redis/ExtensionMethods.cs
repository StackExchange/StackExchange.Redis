using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Pipelines.Sockets.Unofficial.Arenas;

namespace StackExchange.Redis
{
    /// <summary>
    /// Utility methods
    /// </summary>
    public static class ExtensionMethods
    {
        /// <summary>
        /// Create a dictionary from an array of HashEntry values 
        /// </summary>
        /// <param name="hash">The entry to convert to a dictionary.</param>
        public static Dictionary<string,string> ToStringDictionary(this HashEntry[] hash)
        {
            if (hash == null) return null;

            var result = new Dictionary<string, string>(hash.Length, StringComparer.Ordinal);
            for(int i = 0; i < hash.Length; i++)
            {
                result.Add(hash[i].name, hash[i].value);
            }
            return result;
        }
        /// <summary>
        /// Create a dictionary from an array of HashEntry values 
        /// </summary>
        /// <param name="hash">The entry to convert to a dictionary.</param>
        public static Dictionary<RedisValue, RedisValue> ToDictionary(this HashEntry[] hash)
        {
            if (hash == null) return null;

            var result = new Dictionary<RedisValue, RedisValue>(hash.Length);
            for (int i = 0; i < hash.Length; i++)
            {
                result.Add(hash[i].name, hash[i].value);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of SortedSetEntry values 
        /// </summary>
        /// <param name="sortedSet">The set entries to convert to a dictionary.</param>
        public static Dictionary<string, double> ToStringDictionary(this SortedSetEntry[] sortedSet)
        {
            if (sortedSet == null) return null;

            var result = new Dictionary<string, double>(sortedSet.Length, StringComparer.Ordinal);
            for (int i = 0; i < sortedSet.Length; i++)
            {
                result.Add(sortedSet[i].element, sortedSet[i].score);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of SortedSetEntry values 
        /// </summary>
        /// <param name="sortedSet">The set entries to convert to a dictionary.</param>
        public static Dictionary<RedisValue, double> ToDictionary(this SortedSetEntry[] sortedSet)
        {
            if (sortedSet == null) return null;

            var result = new Dictionary<RedisValue, double>(sortedSet.Length);
            for (int i = 0; i < sortedSet.Length; i++)
            {
                result.Add(sortedSet[i].element, sortedSet[i].score);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of key/value pairs
        /// </summary>
        /// <param name="pairs">The pairs to convert to a dictionary.</param>
        public static Dictionary<string, string> ToStringDictionary(this KeyValuePair<RedisKey, RedisValue>[] pairs)
        {
            if (pairs == null) return null;

            var result = new Dictionary<string, string>(pairs.Length, StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i++)
            {
                result.Add(pairs[i].Key, pairs[i].Value);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of key/value pairs
        /// </summary>
        /// <param name="pairs">The pairs to convert to a dictionary.</param>
        public static Dictionary<RedisKey, RedisValue> ToDictionary(this KeyValuePair<RedisKey, RedisValue>[] pairs)
        {
            if (pairs == null) return null;

            var result = new Dictionary<RedisKey, RedisValue>(pairs.Length);
            for (int i = 0; i < pairs.Length; i++)
            {
                result.Add(pairs[i].Key, pairs[i].Value);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of string pairs
        /// </summary>
        /// <param name="pairs">The pairs to convert to a dictionary.</param>
        public static Dictionary<string, string> ToDictionary(this KeyValuePair<string, string>[] pairs)
        {
            if (pairs == null) return null;

            var result = new Dictionary<string, string>(pairs.Length, StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i++)
            {
                result.Add(pairs[i].Key, pairs[i].Value);
            }
            return result;
        }

        /// <summary>
        /// Create an array of RedisValues from an array of strings.
        /// </summary>
        /// <param name="values">The string array to convert to RedisValues</param>
        public static RedisValue[] ToRedisValueArray(this string[] values)
        {
            if (values == null) return null;
            if (values.Length == 0) return Array.Empty<RedisValue>();
            return Array.ConvertAll(values, x => (RedisValue)x);
        }

        /// <summary>
        /// Create an array of strings from an array of values
        /// </summary>
        /// <param name="values">The values to convert to an array.</param>
        public static string[] ToStringArray(this RedisValue[] values)
        {
            if (values == null) return null;
            if (values.Length == 0) return Array.Empty<string>();
            return Array.ConvertAll(values, x => (string)x);
        }

        internal static void AuthenticateAsClient(this SslStream ssl, string host, SslProtocols? allowedProtocols, bool checkCertificateRevocation)
        {
            if (!allowedProtocols.HasValue)
            {
                //Default to the sslProtocols defined by the .NET Framework
                AuthenticateAsClientUsingDefaultProtocols(ssl, host);
                return;
            }

            var certificateCollection = new X509CertificateCollection();
            ssl.AuthenticateAsClient(host, certificateCollection, allowedProtocols.Value, checkCertificateRevocation);
        }

        private static void AuthenticateAsClientUsingDefaultProtocols(SslStream ssl, string host)
        {
            ssl.AuthenticateAsClient(host);
        }

        /// <summary>
        /// Represent a byte-Lease as a read-only Stream
        /// </summary>
        /// <param name="bytes">The lease upon which to base the stream</param>
        /// <param name="ownsLease">If true, disposing the stream also disposes the lease</param>
        public static Stream AsStream(this Lease<byte> bytes, bool ownsLease = true)
        {
            if (bytes == null) return null; // GIGO
            var segment = bytes.ArraySegment;
            if (ownsLease) return new LeaseMemoryStream(segment, bytes);
            return new MemoryStream(segment.Array, segment.Offset, segment.Count, false, true);
        }

        /// <summary>
        /// Decode a byte-Lease as a String, optionally specifying the encoding (UTF-8 if omitted)
        /// </summary>
        /// <param name="bytes">The bytes to decode</param>
        /// <param name="encoding">The encoding to use</param>
        public static string DecodeString(this Lease<byte> bytes, Encoding encoding = null)
        {
            if (bytes == null) return null;
            if (encoding == null) encoding = Encoding.UTF8;
            if (bytes.Length == 0) return "";
            var segment = bytes.ArraySegment;
            return encoding.GetString(segment.Array, segment.Offset, segment.Count);
        }

        /// <summary>
        /// Decode a byte-Lease as a String, optionally specifying the encoding (UTF-8 if omitted)
        /// </summary>
        /// <param name="bytes">The bytes to decode</param>
        /// <param name="encoding">The encoding to use</param>
        public static Lease<char> DecodeLease(this Lease<byte> bytes, Encoding encoding = null)
        {
            if (bytes == null) return null;
            if (encoding == null) encoding = Encoding.UTF8;
            if (bytes.Length == 0) return Lease<char>.Empty;
            var bytesSegment = bytes.ArraySegment;
            var charCount = encoding.GetCharCount(bytesSegment.Array, bytesSegment.Offset, bytesSegment.Count);
            var chars = Lease<char>.Create(charCount, false);
            var charsSegment = chars.ArraySegment;
            encoding.GetChars(bytesSegment.Array, bytesSegment.Offset, bytesSegment.Count,
                charsSegment.Array, charsSegment.Offset);
            return chars;
        }

        private sealed class LeaseMemoryStream : MemoryStream
        {
            private readonly IDisposable _parent;
            public LeaseMemoryStream(ArraySegment<byte> segment, IDisposable parent)
                : base(segment.Array, segment.Offset, segment.Count, false, true)
                => _parent = parent;

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) _parent.Dispose();
            }
        }

        // IMPORTANT: System.Numerics.Vectors is just... broken on .NET with anything < net472; the dependency
        // indirection routinely fails and causes epic levels of fail. We're going to get around this by simply
        // *not using SpanHelpers.IndexOf* (which is what uses it) for net < net472 builds. I've tried every
        // trick (including some that are pure evil), and I can't see a better mechanism. Ultimately, the bindings
        // fail in unusual and unexpected ways, causing:
        //
        //     Could not load file or assembly 'System.Numerics.Vectors, Version=4.1.3.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'
        //     or one of its dependencies.The located assembly's manifest definition does not match the assembly reference. (Exception from HRESULT: 0x80131040)
        //
        // also; note that the nuget tools *do not* reliably (or even occasionally) produce the correct
        // assembly-binding-redirect entries to fix this up, so; it would present an unreasonable support burden
        // otherwise. And yes, I've tried explicitly referencing System.Numerics.Vectors in the manifest to
        // force it... nothing. Nada.

#if VECTOR_SAFE
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int VectorSafeIndexOf(this ReadOnlySpan<byte> span, byte value)
            => span.IndexOf(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int VectorSafeIndexOfCRLF(this ReadOnlySpan<byte> span)
        {
            ReadOnlySpan<byte> CRLF = stackalloc byte[2] { (byte)'\r', (byte)'\n' };
            return span.IndexOf(CRLF);
        }
#else
        internal static int VectorSafeIndexOf(this ReadOnlySpan<byte> span, byte value)
        {
            // yes, this has zero optimization; I'm OK with this as the fallback strategy
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == value) return i;
            }
            return -1;
        }
        internal static int VectorSafeIndexOfCRLF(this ReadOnlySpan<byte> span)
        {
            // yes, this has zero optimization; I'm OK with this as the fallback strategy
            for (int i = 1; i < span.Length; i++)
            {
                if (span[i] == '\n' && span[i-1] == '\r') return i - 1;
            }
            return -1;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T[] ToArray<T>(in this RawResult result, Projection<RawResult, T> selector)
            => result.IsNull ? null : result.GetItems().ToArray(selector);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TTo[] ToArray<TTo, TState>(in this RawResult result, Projection<RawResult, TState, TTo> selector, in TState state)
            => result.IsNull ? null : result.GetItems().ToArray(selector, in state);
    }
}
