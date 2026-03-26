/*
using System;
using StackExchange.Redis;
#pragma warning disable CS8981

namespace StackExchange.Redis.Benchmarks
{
    partial class FastHashSwitch
    {
        static partial class key
        {
            public const int Length = 3;
            public const long HashCS = 7955819;
            public const long HashCI = 5850443;
            public static ReadOnlySpan<byte> U8 => "key"u8;
            public const string Text = "key";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.FastHash.HashCS(value) == HashCS || global::RESPite.FastHash.EqualsCI(value, U8));
        }
        static partial class abc
        {
            public const int Length = 3;
            public const long HashCS = 6513249;
            public const long HashCI = 4407873;
            public static ReadOnlySpan<byte> U8 => "abc"u8;
            public const string Text = "abc";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.FastHash.HashCS(value) == HashCS || global::RESPite.FastHash.EqualsCI(value, U8));
        }
        static partial class port
        {
            public const int Length = 4;
            public const long HashCS = 1953656688;
            public const long HashCI = 1414680400;
            public static ReadOnlySpan<byte> U8 => "port"u8;
            public const string Text = "port";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.FastHash.HashCS(value) == HashCS || global::RESPite.FastHash.EqualsCI(value, U8));
        }
        static partial class test
        {
            public const int Length = 4;
            public const long HashCS = 1953719668;
            public const long HashCI = 1414743380;
            public static ReadOnlySpan<byte> U8 => "test"u8;
            public const string Text = "test";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.FastHash.HashCS(value) == HashCS || global::RESPite.FastHash.EqualsCI(value, U8));
        }
        static partial class tracking_active
        {
            public const int Length = 15;
            public const long HashCS = 7453010343294497396;
            public const long HashCI = 5138124812476043860;
            public static ReadOnlySpan<byte> U8 => "tracking-active"u8;
            public const string Text = "tracking-active";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class sample_ratio
        {
            public const int Length = 12;
            public const long HashCS = 8227343610692854131;
            public const long HashCI = 5912458079874400595;
            public static ReadOnlySpan<byte> U8 => "sample-ratio"u8;
            public const string Text = "sample-ratio";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class selected_slots
        {
            public const int Length = 14;
            public const long HashCS = 7234316346692756851;
            public const long HashCI = 4919430815874303315;
            public static ReadOnlySpan<byte> U8 => "selected-slots"u8;
            public const string Text = "selected-slots";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class all_commands_all_slots_us
        {
            public const int Length = 25;
            public const long HashCS = 7885080994350132321;
            public const long HashCI = 5570195463531678785;
            public static ReadOnlySpan<byte> U8 => "all-commands-all-slots-us"u8;
            public const string Text = "all-commands-all-slots-us";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class all_commands_selected_slots_us
        {
            public const int Length = 30;
            public const long HashCS = 7885080994350132321;
            public const long HashCI = 5570195463531678785;
            public static ReadOnlySpan<byte> U8 => "all-commands-selected-slots-us"u8;
            public const string Text = "all-commands-selected-slots-us";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class sampled_command_selected_slots_us
        {
            public const int Length = 33;
            public const long HashCS = 3270850745794912627;
            public const long HashCI = 955965214976459091;
            public static ReadOnlySpan<byte> U8 => "sampled-command-selected-slots-us"u8;
            public const string Text = "sampled-command-selected-slots-us";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class sampled_commands_selected_slots_us
        {
            public const int Length = 34;
            public const long HashCS = 3270850745794912627;
            public const long HashCI = 955965214976459091;
            public static ReadOnlySpan<byte> U8 => "sampled-commands-selected-slots-us"u8;
            public const string Text = "sampled-commands-selected-slots-us";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class net_bytes_all_commands_all_slots
        {
            public const int Length = 32;
            public const long HashCS = 7310601557705516398;
            public const long HashCI = 4995716026887062862;
            public static ReadOnlySpan<byte> U8 => "net-bytes-all-commands-all-slots"u8;
            public const string Text = "net-bytes-all-commands-all-slots";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class net_bytes_all_commands_selected_slots
        {
            public const int Length = 37;
            public const long HashCS = 7310601557705516398;
            public const long HashCI = 4995716026887062862;
            public static ReadOnlySpan<byte> U8 => "net-bytes-all-commands-selected-slots"u8;
            public const string Text = "net-bytes-all-commands-selected-slots";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class net_bytes_sampled_commands_selected_slots
        {
            public const int Length = 41;
            public const long HashCS = 7310601557705516398;
            public const long HashCI = 4995716026887062862;
            public static ReadOnlySpan<byte> U8 => "net-bytes-sampled-commands-selected-slots"u8;
            public const string Text = "net-bytes-sampled-commands-selected-slots";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class collection_start_time_unix_ms
        {
            public const int Length = 29;
            public const long HashCS = 7598807758542761827;
            public const long HashCI = 5283922227724308291;
            public static ReadOnlySpan<byte> U8 => "collection-start-time-unix-ms"u8;
            public const string Text = "collection-start-time-unix-ms";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class collection_duration_ms
        {
            public const int Length = 22;
            public const long HashCS = 7598807758542761827;
            public const long HashCI = 5283922227724308291;
            public static ReadOnlySpan<byte> U8 => "collection-duration-ms"u8;
            public const string Text = "collection-duration-ms";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class collection_duration_us
        {
            public const int Length = 22;
            public const long HashCS = 7598807758542761827;
            public const long HashCI = 5283922227724308291;
            public static ReadOnlySpan<byte> U8 => "collection-duration-us"u8;
            public const string Text = "collection-duration-us";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class total_cpu_time_user_ms
        {
            public const int Length = 22;
            public const long HashCS = 8098366498457022324;
            public const long HashCI = 5783480967638568788;
            public static ReadOnlySpan<byte> U8 => "total-cpu-time-user-ms"u8;
            public const string Text = "total-cpu-time-user-ms";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class total_cpu_time_user_us
        {
            public const int Length = 22;
            public const long HashCS = 8098366498457022324;
            public const long HashCI = 5783480967638568788;
            public static ReadOnlySpan<byte> U8 => "total-cpu-time-user-us"u8;
            public const string Text = "total-cpu-time-user-us";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class total_cpu_time_sys_ms
        {
            public const int Length = 21;
            public const long HashCS = 8098366498457022324;
            public const long HashCI = 5783480967638568788;
            public static ReadOnlySpan<byte> U8 => "total-cpu-time-sys-ms"u8;
            public const string Text = "total-cpu-time-sys-ms";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class total_cpu_time_sys_us
        {
            public const int Length = 21;
            public const long HashCS = 8098366498457022324;
            public const long HashCI = 5783480967638568788;
            public static ReadOnlySpan<byte> U8 => "total-cpu-time-sys-us"u8;
            public const string Text = "total-cpu-time-sys-us";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class total_net_bytes
        {
            public const int Length = 15;
            public const long HashCS = 7308829188783632244;
            public const long HashCI = 4993943657965178708;
            public static ReadOnlySpan<byte> U8 => "total-net-bytes"u8;
            public const string Text = "total-net-bytes";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class by_cpu_time_us
        {
            public const int Length = 14;
            public const long HashCS = 8371476407912331618;
            public const long HashCI = 6056590877093878082;
            public static ReadOnlySpan<byte> U8 => "by-cpu-time-us"u8;
            public const string Text = "by-cpu-time-us";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
        static partial class by_net_bytes
        {
            public const int Length = 12;
            public const long HashCS = 7074438568657910114;
            public const long HashCI = 4759553037839456578;
            public static ReadOnlySpan<byte> U8 => "by-net-bytes"u8;
            public const string Text = "by-net-bytes";
            public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
            public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
        }
    }
}
*/
