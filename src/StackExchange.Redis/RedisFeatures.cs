using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace StackExchange.Redis
{
    /// <summary>
    /// Provides basic information about the features available on a particular version of Redis
    /// </summary>
    public readonly struct RedisFeatures
    {
        internal static readonly Version v2_0_0 = new Version(2, 0, 0),
                                         v2_1_0 = new Version(2, 1, 0),
                                         v2_1_1 = new Version(2, 1, 1),
                                         v2_1_2 = new Version(2, 1, 2),
                                         v2_1_3 = new Version(2, 1, 3),
                                         v2_1_8 = new Version(2, 1, 8),
                                         v2_2_0 = new Version(2, 2, 0),
                                         v2_4_0 = new Version(2, 4, 0),
                                         v2_5_7 = new Version(2, 5, 7),
                                         v2_5_10 = new Version(2, 5, 10),
                                         v2_5_14 = new Version(2, 5, 14),
                                         v2_6_0 = new Version(2, 6, 0),
                                         v2_6_5 = new Version(2, 6, 5),
                                         v2_6_9 = new Version(2, 6, 9),
                                         v2_6_12 = new Version(2, 6, 12),
                                         v2_8_0 = new Version(2, 8, 0),
                                         v2_8_12 = new Version(2, 8, 12),
                                         v2_8_18 = new Version(2, 8, 18),
                                         v2_9_5 = new Version(2, 9, 5),
                                         v3_0_0 = new Version(3, 0, 0),
                                         v3_2_0 = new Version(3, 2, 0),
                                         v4_0_0 = new Version(4, 0, 0),
                                         v4_9_1 = new Version(4, 9, 1); // 5.0 RC1 is version 4.9.1

        private readonly Version version;

        /// <summary>
        /// Create a new RedisFeatures instance for the given version
        /// </summary>
        /// <param name="version">The version of redis to base the feature set on.</param>
        public RedisFeatures(Version version)
        {
            this.version = version ?? throw new ArgumentNullException(nameof(version));
        }

        /// <summary>
        /// Does BITOP / BITCOUNT exist?
        /// </summary>
        public bool BitwiseOperations => Version >= v2_5_10;

        /// <summary>
        /// Is CLIENT SETNAME available?
        /// </summary>
        public bool ClientName => Version >= v2_6_9;

        /// <summary>
        /// Does EXEC support EXECABORT if there are errors?
        /// </summary>
        public bool ExecAbort => Version >= v2_6_5 && Version != v2_9_5;

        /// <summary>
        /// Can EXPIRE be used to set expiration on a key that is already volatile (i.e. has an expiration)?
        /// </summary>
        public bool ExpireOverwrite => Version >= v2_1_3;

        /// <summary>
        /// Is HSTRLEN available?
        /// </summary>
        public bool HashStringLength => Version >= v3_2_0;

        /// <summary>
        /// Does HDEL support varadic usage?
        /// </summary>
        public bool HashVaradicDelete => Version >= v2_4_0;

        /// <summary>
        /// Does INCRBYFLOAT / HINCRBYFLOAT exist?
        /// </summary>
        public bool IncrementFloat => Version >= v2_5_7;

        /// <summary>
        /// Does INFO support sections?
        /// </summary>
        public bool InfoSections => Version >= v2_8_0;

        /// <summary>
        /// Is LINSERT available?
        /// </summary>
        public bool ListInsert => Version >= v2_1_1;

        /// <summary>
        /// Is MEMORY available?
        /// </summary>
        public bool Memory => Version >= v4_0_0;

        /// <summary>
        /// Indicates whether PEXPIRE and PTTL are supported
        /// </summary>
        public bool MillisecondExpiry => Version >= v2_6_0;

        /// <summary>
        /// Does SRANDMEMBER support "count"?
        /// </summary>
        public bool MultipleRandom => Version >= v2_5_14;

        /// <summary>
        /// Is the PERSIST operation supported?
        /// </summary>
        public bool Persist => Version >= v2_1_2;

        /// <summary>
        /// Is RPUSHX and LPUSHX available?
        /// </summary>
        public bool PushIfNotExists => Version >= v2_1_1;

        /// <summary>
        /// Are cursor-based scans available?
        /// </summary>
        public bool Scan => Version >= v2_8_0;

        /// <summary>
        /// Does EVAL / EVALSHA / etc exist?
        /// </summary>
        public bool Scripting => Version >= v2_5_7;

        /// <summary>
        /// Does SET have the EX|PX|NX|XX extensions?
        /// </summary>
        public bool SetConditional => Version >= v2_6_12;

        /// <summary>
        /// Does SADD support varadic usage?
        /// </summary>
        public bool SetVaradicAddRemove => Version >= v2_4_0;

        /// <summary>
        /// Is ZPOPMAX and ZPOPMIN available?
        /// </summary>
        public bool SortedSetPop => Version >= v4_9_1;

        /// <summary>
        /// Are Redis Streams available?
        /// </summary>
        public bool Streams => Version >= v4_9_1;

        /// <summary>
        /// Is STRLEN available?
        /// </summary>
        public bool StringLength => Version >= v2_1_2;

        /// <summary>
        /// Is SETRANGE available?
        /// </summary>
        public bool StringSetRange => Version >= v2_1_8;

        /// <summary>
        /// Is SWAPDB available?
        /// </summary>
        public bool SwapDB => Version >= v4_0_0;

        /// <summary>
        /// Does TIME exist?
        /// </summary>
        public bool Time => Version >= v2_6_0;

        /// <summary>
        /// Does UNLINK exist?
        /// </summary>
        public bool Unlink => Version >= v4_0_0;

        /// <summary>
        /// Are Lua changes to the calling database transparent to the calling client?
        /// </summary>
        public bool ScriptingDatabaseSafe => Version >= v2_8_12;

        /// <summary>
        /// Is PFCOUNT supported on slaves?
        /// </summary>
        public bool HyperLogLogCountSlaveSafe => Version >= v2_8_18;

        /// <summary>
        /// Are the GEO commands available?
        /// </summary>
        public bool Geo => Version >= v3_2_0;

        /// <summary>
        /// Can PING be used on a subscription connection?
        /// </summary>
        internal bool PingOnSubscriber => Version >= v3_0_0;

        /// <summary>
        /// Does SetPop support popping multiple items?
        /// </summary>
        public bool SetPopMultiple => Version >= v3_2_0;

        /// <summary>
        /// The Redis version of the server
        /// </summary>
        public Version Version => version ?? v2_0_0;

        /// <summary>
        /// Create a string representation of the available features
        /// </summary>
        public override string ToString()
        {
            var v = Version; // the docs lie: Version.ToString(fieldCount) only supports 0-2 fields
            var sb = new StringBuilder().Append("Features in ").Append(v.Major).Append('.').Append(v.Minor);
            if (v.Revision >= 0) sb.Append('.').Append(v.Revision);
            if (v.Build >= 0) sb.Append('.').Append(v.Build);
            sb.AppendLine();
            object boxed = this;
            foreach(var prop in s_props)
            {
                sb.Append(prop.Name).Append(": ").Append(prop.GetValue(boxed)).AppendLine();
            }
            return sb.ToString();
        }

        private static readonly PropertyInfo[] s_props = (
            from prop in typeof(RedisFeatures).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            where prop.PropertyType == typeof(bool)
            let indexers = prop.GetIndexParameters()
            where indexers == null || indexers.Length == 0
            orderby prop.Name
            select prop).ToArray();

        /// <summary>Returns the hash code for this instance.</summary>
        /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
        public override int GetHashCode() => Version.GetHashCode();
        /// <summary>Indicates whether this instance and a specified object are equal.</summary>
        /// <returns>true if <paramref name="obj" /> and this instance are the same type and represent the same value; otherwise, false. </returns>
        /// <param name="obj">The object to compare with the current instance. </param>
        public override bool Equals(object obj) => obj is RedisFeatures f && f.Version == Version;
    }
}
