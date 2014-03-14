//using System;

//namespace StackExchange.Redis
//{
//    /// <summary>
//    /// Wraps and unwraps keyspaces automatically
//    /// </summary>
//    public interface IChannelWrapper
//    {
//        /// <summary>
//        /// Encode a key (translates a vanilla key to a wrapped key)
//        /// </summary>
//        RedisValue Encode(RedisValue key);
//        /// <summary>
//        /// Decode a key (translates a wrapped key to a vanilla key)
//        /// </summary>
//        RedisValue Decode(RedisValue key);
//    }
//    /// <summary>
//    /// Implements IKeyspaceWrapper using a prefix notation
//    /// </summary>
//    public class ChannelPrefixWrapper : IChannelWrapper
//    {
//        private readonly byte[] prefix;
//        /// <summary>
//        /// Create a new KeyspacePrefixWrapper instance
//        /// </summary>
//        /// <param name="prefix">The prefix to the keys</param>
//        public ChannelPrefixWrapper(RedisValue prefix)
//        {
//            this.prefix = prefix;
//            if(this.prefix != null && this.prefix.Length == 0)
//            {
//                this.prefix = null; // no need to do anything
//            }
//        }

//        /// <summary>
//        /// Encode a key (translates a vanilla key to a wrapped key)
//        /// </summary>
//        public virtual RedisValue Decode(RedisValue key)
//        {
//            if (prefix == null) return key;

//            byte[] blob = key;
//            if (blob == null || blob.Length == 0) return key; // preserve nulls
            
//            if(AssertStarts(blob, prefix))
//            {
//                var result = new byte[blob.Length - prefix.Length];
//                Buffer.BlockCopy(blob, prefix.Length, result, 0, result.Length);
//                return result;
//            }
//            return key;
//        }

//        internal static bool AssertStarts(byte[] value, byte[] expected)
//        {
//            for (int i = 0; i < expected.Length; i++)
//            {
//                if (expected[i] != value[i]) return false;
//            }
//            return true;
//        }

//        /// <summary>
//        /// Decode a key (translates a wrapped key to a vanilla key)
//        /// </summary>
//        public virtual RedisValue Encode(RedisValue key)
//        {
//            if (prefix == null) return key;
            
//            byte[] blob = key;
//            if (blob == null) return key; // preserve nulls
//            if (blob.Length == 0) return prefix;

//            var result = new byte[prefix.Length + blob.Length];
//            prefix.CopyTo(result, 0);
//            blob.CopyTo(result, prefix.Length);
//            return result;
//        }
//    }
//}
