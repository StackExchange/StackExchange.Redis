using System;

namespace StackExchange.Redis
{
#pragma warning disable SA1310 // Field names should not contain underscore
#pragma warning disable SA1311 // Static readonly fields should begin with upper-case letter
    internal static partial class CommonReplies
    {
        public static readonly CommandBytes
            ASK = "ASK ",
            authFail_trimmed = CommandBytes.TrimToFit("ERR operation not permitted"),
            backgroundSavingStarted_trimmed = CommandBytes.TrimToFit("Background saving started"),
            backgroundSavingAOFStarted_trimmed =
                CommandBytes.TrimToFit("Background append only file rewriting started"),
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
            replica_read_only = "replica-read-only",
            slave_read_only = "slave-read-only",
            timeout = "timeout",
            wildcard = "*",
            WRONGPASS = "WRONGPASS",
            yes = "yes",
            zero = "0",

            // HELLO
            version = "version",
            proto = "proto",
            role = "role",
            mode = "mode",
            id = "id";
    }

    internal static partial class CommonRepliesHash
    {
#pragma warning disable CS8981, SA1300, SA1134 // forgive naming
        // ReSharper disable InconsistentNaming
        [FastHash] internal static partial class length { }
        [FastHash] internal static partial class radix_tree_keys { }
        [FastHash] internal static partial class radix_tree_nodes { }
        [FastHash] internal static partial class last_generated_id { }
        [FastHash] internal static partial class max_deleted_entry_id { }
        [FastHash] internal static partial class entries_added { }
        [FastHash] internal static partial class recorded_first_entry_id { }
        [FastHash] internal static partial class idmp_duration { }
        [FastHash] internal static partial class idmp_maxsize { }
        [FastHash] internal static partial class pids_tracked { }
        [FastHash] internal static partial class first_entry { }
        [FastHash] internal static partial class last_entry { }
        [FastHash] internal static partial class groups { }
        [FastHash] internal static partial class iids_tracked { }
        [FastHash] internal static partial class iids_added { }
        [FastHash] internal static partial class iids_duplicates { }

        // ReSharper restore InconsistentNaming
#pragma warning restore CS8981, SA1300, SA1134 // forgive naming
    }

    internal static class RedisLiterals
    {
        // unlike primary commands, these do not get altered by the command-map; we may as
        // well compute the bytes once and share them
        public static readonly RedisValue
            ACLCAT = "ACLCAT",
            ADDR = "ADDR",
            AFTER = "AFTER",
            AGGREGATE = "AGGREGATE",
            ALPHA = "ALPHA",
            AND = "AND",
            ANDOR = "ANDOR",
            ANY = "ANY",
            ASC = "ASC",
            BEFORE = "BEFORE",
            BIT = "BIT",
            BY = "BY",
            BYLEX = "BYLEX",
            BYSCORE = "BYSCORE",
            BYTE = "BYTE",
            CH = "CH",
            CHANNELS = "CHANNELS",
            COUNT = "COUNT",
            DB = "DB",
            @default = "default",
            DESC = "DESC",
            DIFF = "DIFF",
            DIFF1 = "DIFF1",
            DOCTOR = "DOCTOR",
            ENCODING = "ENCODING",
            EX = "EX",
            EXAT = "EXAT",
            EXISTS = "EXISTS",
            FIELDS = "FIELDS",
            FILTERBY = "FILTERBY",
            FLUSH = "FLUSH",
            FNX = "FNX",
            FREQ = "FREQ",
            FXX = "FXX",
            GET = "GET",
            GETKEYS = "GETKEYS",
            GETNAME = "GETNAME",
            GT = "GT",
            HISTORY = "HISTORY",
            ID = "ID",
            IDX = "IDX",
            IDLETIME = "IDLETIME",
            IDMP = "IDMP",
            IDMPAUTO = "IDMPAUTO",
            IDMP_DURATION = "IDMP-DURATION",
            IDMP_MAXSIZE = "IDMP-MAXSIZE",
            KEEPTTL = "KEEPTTL",
            KILL = "KILL",
            LADDR = "LADDR",
            LATEST = "LATEST",
            LEFT = "LEFT",
            LEN = "LEN",
            lib_name = "lib-name",
            lib_ver = "lib-ver",
            LIMIT = "LIMIT",
            LIST = "LIST",
            LT = "LT",
            MATCH = "MATCH",
            MALLOC_STATS = "MALLOC-STATS",
            MAX = "MAX",
            MAXAGE = "MAXAGE",
            MAXLEN = "MAXLEN",
            MIN = "MIN",
            MINMATCHLEN = "MINMATCHLEN",
            MODULE = "MODULE",
            NODES = "NODES",
            NOSAVE = "NOSAVE",
            NOT = "NOT",
            NOVALUES = "NOVALUES",
            NUMPAT = "NUMPAT",
            NUMSUB = "NUMSUB",
            NX = "NX",
            OBJECT = "OBJECT",
            ONE = "ONE",
            OR = "OR",
            PATTERN = "PATTERN",
            PAUSE = "PAUSE",
            PERSIST = "PERSIST",
            PING = "PING",
            PURGE = "PURGE",
            PX = "PX",
            PXAT = "PXAT",
            RANK = "RANK",
            REFCOUNT = "REFCOUNT",
            REPLACE = "REPLACE",
            RESET = "RESET",
            RESETSTAT = "RESETSTAT",
            REV = "REV",
            REWRITE = "REWRITE",
            RIGHT = "RIGHT",
            SAVE = "SAVE",
            SEGFAULT = "SEGFAULT",
            SET = "SET",
            SETINFO = "SETINFO",
            SETNAME = "SETNAME",
            SKIPME = "SKIPME",
            STATS = "STATS",
            STOP = "STOP",
            STORE = "STORE",
            TYPE = "TYPE",
            USERNAME = "USERNAME",
            WEIGHTS = "WEIGHTS",
            WITHMATCHLEN = "WITHMATCHLEN",
            WITHSCORES = "WITHSCORES",
            WITHVALUES = "WITHVALUES",
            XOR = "XOR",
            XX = "XX",

            // Sentinel Literals
            MASTERS = "MASTERS",
            MASTER = "MASTER",
            REPLICAS = "REPLICAS",
            SLAVES = "SLAVES",
            GETMASTERADDRBYNAME = "GET-MASTER-ADDR-BY-NAME",
            // RESET = "RESET",
            FAILOVER = "FAILOVER",
            SENTINELS = "SENTINELS",

            // Sentinel Literals as of 2.8.4
            MONITOR = "MONITOR",
            REMOVE = "REMOVE",
            // SET = "SET",

            // replication states
            connect = "connect",
            connected = "connected",
            connecting = "connecting",
            handshake = "handshake",
            none = "none",
            sync = "sync",

            MinusSymbol = "-",
            PlusSymbol = "+",
            Wildcard = "*",

            // Geo Radius/Search Literals
            BYBOX = "BYBOX",
            BYRADIUS = "BYRADIUS",
            FROMMEMBER = "FROMMEMBER",
            FROMLONLAT = "FROMLONLAT",
            STOREDIST = "STOREDIST",
            WITHCOORD = "WITHCOORD",
            WITHDIST = "WITHDIST",
            WITHHASH = "WITHHASH",

            // geo units
            ft = "ft",
            km = "km",
            m = "m",
            mi = "mi",

            // misc (config, etc)
            databases = "databases",
            master = "master",
            no = "no",
            normal = "normal",
            pubsub = "pubsub",
            replica = "replica",
            replica_read_only = "replica-read-only",
            replication = "replication",
            sentinel = "sentinel",
            server = "server",
            slave = "slave",
            slave_read_only = "slave-read-only",
            timeout = "timeout",
            yes = "yes";

        internal static RedisValue Get(Bitwise operation) => operation switch
        {
            Bitwise.And => AND,
            Bitwise.Or => OR,
            Bitwise.Xor => XOR,
            Bitwise.Not => NOT,
            Bitwise.Diff => DIFF,
            Bitwise.Diff1 => DIFF1,
            Bitwise.AndOr => ANDOR,
            Bitwise.One => ONE,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }
#pragma warning restore SA1310 // Field names should not contain underscore
#pragma warning restore SA1311 // Static readonly fields should begin with upper-case letter
}
