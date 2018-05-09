using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

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

        private static readonly string[] nix = new string[0];
        /// <summary>
        /// Create an array of strings from an array of values
        /// </summary>
        /// <param name="values">The values to convert to an array.</param>
        public static string[] ToStringArray(this RedisValue[] values)
        {
            if (values == null) return null;
            if (values.Length == 0) return nix;
            return ConvertHelper.ConvertAll(values, x => (string)x);
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
#if NETSTANDARD1_5
            ssl.AuthenticateAsClientAsync(host, certificateCollection, allowedProtocols.Value, checkCertRevocation)
                                .GetAwaiter().GetResult();
#else
            ssl.AuthenticateAsClient(host, certificateCollection, allowedProtocols.Value, checkCertRevocation);
#endif
        }

        private static void AuthenticateAsClientUsingDefaultProtocols(SslStream ssl, string host)
        {
#if NETSTANDARD1_5
            ssl.AuthenticateAsClientAsync(host).GetAwaiter().GetResult();
#else
            ssl.AuthenticateAsClient(host);
#endif
        }
    }
}
