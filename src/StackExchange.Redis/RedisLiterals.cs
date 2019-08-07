using System;
using System.Text;

namespace StackExchange.Redis
{
    internal static class CommonReplies
    {
        public static readonly CommandBytes
            ASK = "ASK ",
            authFail_trimmed = CommandBytes.TrimToFit("ERR operation not permitted"),
            backgroundSavingStarted_trimmed = CommandBytes.TrimToFit("Background saving started"),
            databases = "databases",
            loading = "LOADING ",
            MOVED = "MOVED ",
            NOAUTH = "NOAUTH ",
            NOSCRIPT = "NOSCRIPT ",
            no = "no",
            OK = "OK",
            one = "1",
            PONG = "PONG",
            QUEUED = "QUEUED",
            READONLY = "READONLY ",
            slave_read_only = "slave-read-only",
            timeout = "timeout",
            wildcard = "*",
            yes = "yes",
            zero = "0",

            // streams
            length = "length",
            radixTreeKeys = "radix-tree-keys",
            radixTreeNodes = "radix-tree-nodes",
            groups = "groups",
            lastGeneratedId = "last-generated-id",
            firstEntry = "first-entry",
            lastEntry = "last-entry";
    }
    internal static class RedisLiterals
    {
        // unlike primary commands, these do not get altered by the command-map; we may as
        // well compute the bytes once and share them
        public static readonly RedisValue
            ADDR = "ADDR",
            AFTER = "AFTER",
            AGGREGATE = "AGGREGATE",
            ALPHA = "ALPHA",
            AND = "AND",
            BEFORE = "BEFORE",
            BY = "BY",
            CHANNELS = "CHANNELS",
            COPY = "COPY",
            COUNT = "COUNT",
            DESC = "DESC",
            DOCTOR = "DOCTOR",
            EX = "EX",
            EXISTS = "EXISTS",
            FLUSH = "FLUSH",
            GET = "GET",
            GETNAME = "GETNAME",
            HISTORY = "HISTORY",
            ID = "ID",
            IDLETIME = "IDLETIME",
            KILL = "KILL",
            LATEST = "LATEST",
            LIMIT = "LIMIT",
            LIST = "LIST",
            LOAD = "LOAD",
            MATCH = "MATCH",
            MALLOC_STATS = "MALLOC-STATS",
            MAX = "MAX",
            MIN = "MIN",
            NODES = "NODES",
            NOSAVE = "NOSAVE",
            NOT = "NOT",
            NUMPAT = "NUMPAT",
            NUMSUB = "NUMSUB",
            NX = "NX",
            OBJECT = "OBJECT",
            OR = "OR",
            PAUSE = "PAUSE",
            PING = "PING",
            PURGE = "PURGE",
            PX = "PX",
            REPLACE = "REPLACE",
            RESET = "RESET",
            RESETSTAT = "RESETSTAT",
            REWRITE = "REWRITE",
            SAVE = "SAVE",
            SEGFAULT = "SEGFAULT",
            SET = "SET",
            SETNAME = "SETNAME",
            SKIPME = "SKIPME",
            STATS = "STATS",
            STORE = "STORE",
            TYPE = "TYPE",
            WEIGHTS = "WEIGHTS",
            WITHSCORES = "WITHSCORES",
            XOR = "XOR",
            XX = "XX",

            // Sentinel Literals
            MASTERS = "MASTERS",
            MASTER = "MASTER",
            SLAVES = "SLAVES",
            GETMASTERADDRBYNAME = "GET-MASTER-ADDR-BY-NAME",
            //            RESET = "RESET",
            FAILOVER = "FAILOVER",
            SENTINELS = "SENTINELS",

            // Sentinel Literals as of 2.8.4
            MONITOR = "MONITOR",
            REMOVE = "REMOVE",
            //            SET = "SET",

            MinusSymbol = "-",
            PlusSumbol = "+",
            Wildcard = "*",

            // misc (config, etc)
            databases = "databases",
            no = "no",
            normal = "normal",
            pubsub = "pubsub",
            replication = "replication",
            server = "server",
            slave = "slave",
            slave_read_only = "slave-read-only",
            timeout = "timeout",
            yes = "yes";

        internal static RedisValue Get(Bitwise operation)
        {
            switch (operation)
            {
                case Bitwise.And: return AND;
                case Bitwise.Or: return OR;
                case Bitwise.Xor: return XOR;
                case Bitwise.Not: return NOT;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }
}
