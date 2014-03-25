using System;
using System.Text;

namespace StackExchange.Redis
{
    internal static class RedisLiterals
    {
        // unlike primary commands, these do not get altered by the command-map; we may as
        // well compute the bytes once and share them
        public static readonly RedisValue
            BEFORE = "BEFORE",
            AFTER = "AFTER",
            CHANNELS = "CHANNELS",
            GET = "GET",
            SET = "SET",
            SEGFAULT = "SEGFAULT",
            PAUSE = "PAUSE",
            SETNAME = "SETNAME",
            GETNAME = "GETNAME",
            NUMPAT = "NUMPAT",
            NUMSUB = "NUMSUB",
            LIST = "LIST",
            NODES = "NODES",
            COUNT = "COUNT",
            MATCH = "MATCH",
            OBJECT = "OBJECT",
            REWRITE = "REWRITE",
            KILL = "KILL",
            SAVE = "SAVE",
            NOSAVE = "NOSAVE",
            RESET = "RESET",
            NX = "NX",
            PX = "PX",
            EX = "EX",
            XX = "XX",
            WITHSCORES = "WITHSCORES",
            LIMIT = "LIMIT",
            AND = "AND",
            OR = "OR",
            NOT = "NOT",
            XOR = "XOR",
            RESETSTAT = "RESETSTAT",
            BY = "BY",
            DESC = "DESC",
            ALPHA = "ALPHA",
            STORE = "STORE",
            WEIGHTS = "WEIGHTS",
            MIN = "MIN",
            MAX = "MAX",
            AGGREGATE = "AGGREGATE",
            LOAD = "LOAD",
            EXISTS = "EXISTS",
            FLUSH = "FLUSH",
            PING = "PING",

            // DO NOT CHANGE CASE: these are configuration settings and MUST be as-is
            databases = "databases",
            timeout = "timeout",
            slave_read_only = "slave-read-only",
            yes = "yes",
            no = "no",
            replication = "replication",
            server = "server",
            Wildcard = "*";

        public static readonly byte[] BytesOK = Encoding.UTF8.GetBytes("OK");
        public static readonly byte[] ByteWildcard = { (byte)'*' };
        public static readonly byte[] BytesPONG = Encoding.UTF8.GetBytes("PONG");


        internal static RedisValue Get(Bitwise operation)
        {
            switch(operation)
            {
                case Bitwise.And: return AND;
                case Bitwise.Or: return OR;
                case Bitwise.Xor: return XOR;
                case Bitwise.Not: return NOT;
                default: throw new ArgumentOutOfRangeException("operation");
            }
        }
    }
}