using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

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

        internal static void AuthenticateAsClient(this SslStream ssl, string host, SslProtocols? allowedProtocols)
        {
            if (!allowedProtocols.HasValue)
            {
                //Default to the sslProtocols defined by the .NET Framework
                AuthenticateAsClientUsingDefaultProtocols(ssl, host);
                return;
            }

            var certificateCollection = new X509CertificateCollection();
            const bool checkCertRevocation = true;
            ssl.AuthenticateAsClient(host, certificateCollection, allowedProtocols.Value, checkCertRevocation);
        }

        private static void AuthenticateAsClientUsingDefaultProtocols(SslStream ssl, string host)
        {
            ssl.AuthenticateAsClient(host);
        }

        /// <summary>
        /// Represent a byte-Lease as a Stream
        /// </summary>
        /// <param name="bytes">The lease upon which to base the stream</param>
        /// <param name="ownsLease">If true, disposing the stream also disposes the lease</param>
        public static MemoryStream AsStream(this Lease<byte> bytes, bool ownsLease = true)
        {
            if (bytes == null) return null; // GIGO
            var segment = bytes.ArraySegment;
            if (ownsLease) return new LeaseMemoryStream(segment, bytes);
            return new MemoryStream(segment.Array, segment.Offset, segment.Count, true, true);
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
                : base(segment.Array, segment.Offset, segment.Count, true, true)
                => _parent = parent;

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) _parent.Dispose();
            }
        }
    }
}
