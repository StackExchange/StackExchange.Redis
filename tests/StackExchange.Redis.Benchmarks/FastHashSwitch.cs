using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using RESPite;
// ReSharper disable InconsistentNaming
// ReSharper disable ArrangeTypeMemberModifiers
// ReSharper disable MemberCanBePrivate.Local
#pragma warning disable SA1300, SA1134, CS8981, SA1400
namespace StackExchange.Redis.Benchmarks;

[ShortRunJob, MemoryDiagnoser]
public class FastHashSwitch
{
    // conclusion: it doesn't matter; switch on the hash or length is fine, just: remember to do the Is check
    public enum Field
    {
        key,
        abc,
        port,
        test,
        tracking_active,
        sample_ratio,
        selected_slots,
        all_commands_all_slots_us,
        all_commands_selected_slots_us,
        sampled_command_selected_slots_us,
        sampled_commands_selected_slots_us,
        net_bytes_all_commands_all_slots,
        net_bytes_all_commands_selected_slots,
        net_bytes_sampled_commands_selected_slots,
        collection_start_time_unix_ms,
        collection_duration_ms,
        collection_duration_us,
        total_cpu_time_user_ms,
        total_cpu_time_user_us,
        total_cpu_time_sys_ms,
        total_cpu_time_sys_us,
        total_net_bytes,
        by_cpu_time_us,
        by_net_bytes,

        Unknown = -1,
    }

    private byte[] _bytes = [];
    [GlobalSetup]
    public void Init() => _bytes = Encoding.UTF8.GetBytes(Value);

    public static string[] GetValues() =>
    [
        key.Text,
        abc.Text,
        port.Text,
        test.Text,
        tracking_active.Text,
        sample_ratio.Text,
        selected_slots.Text,
        all_commands_all_slots_us.Text,
        net_bytes_sampled_commands_selected_slots.Text,
        total_cpu_time_sys_us.Text,
        total_net_bytes.Text,
        by_cpu_time_us.Text,
        by_net_bytes.Text,
        "miss",
        "PORT",
        "much longer miss",
    ];

    [ParamsSource(nameof(GetValues))]
    public string Value { get; set; } = "";

    [Benchmark]
    public Field SwitchOnHash()
    {
        ReadOnlySpan<byte> span = _bytes;
        var hash = FastHash.HashCS(span);
        return hash switch
        {
            key.HashCS when key.IsCS(hash, span) => Field.key,
            abc.HashCS when abc.IsCS(hash, span) => Field.abc,
            port.HashCS when port.IsCS(hash, span) => Field.port,
            test.HashCS when test.IsCS(hash, span) => Field.test,
            tracking_active.HashCS when tracking_active.IsCS(hash, span) => Field.tracking_active,
            sample_ratio.HashCS when sample_ratio.IsCS(hash, span) => Field.sample_ratio,
            selected_slots.HashCS when selected_slots.IsCS(hash, span) => Field.selected_slots,
            all_commands_all_slots_us.HashCS when all_commands_all_slots_us.IsCS(hash, span) => Field.all_commands_all_slots_us,
            all_commands_selected_slots_us.HashCS when all_commands_selected_slots_us.IsCS(hash, span) => Field.all_commands_selected_slots_us,
            sampled_command_selected_slots_us.HashCS when sampled_command_selected_slots_us.IsCS(hash, span) => Field.sampled_command_selected_slots_us,
            sampled_commands_selected_slots_us.HashCS when sampled_commands_selected_slots_us.IsCS(hash, span) => Field.sampled_commands_selected_slots_us,
            net_bytes_all_commands_all_slots.HashCS when net_bytes_all_commands_all_slots.IsCS(hash, span) => Field.net_bytes_all_commands_all_slots,
            net_bytes_all_commands_selected_slots.HashCS when net_bytes_all_commands_selected_slots.IsCS(hash, span) => Field.net_bytes_all_commands_selected_slots,
            net_bytes_sampled_commands_selected_slots.HashCS when net_bytes_sampled_commands_selected_slots.IsCS(hash, span) => Field.net_bytes_sampled_commands_selected_slots,
            collection_start_time_unix_ms.HashCS when collection_start_time_unix_ms.IsCS(hash, span) => Field.collection_start_time_unix_ms,
            collection_duration_ms.HashCS when collection_duration_ms.IsCS(hash, span) => Field.collection_duration_ms,
            collection_duration_us.HashCS when collection_duration_us.IsCS(hash, span) => Field.collection_duration_us,
            total_cpu_time_user_ms.HashCS when total_cpu_time_user_ms.IsCS(hash, span) => Field.total_cpu_time_user_ms,
            total_cpu_time_user_us.HashCS when total_cpu_time_user_us.IsCS(hash, span) => Field.total_cpu_time_user_us,
            total_cpu_time_sys_ms.HashCS when total_cpu_time_sys_ms.IsCS(hash, span) => Field.total_cpu_time_sys_ms,
            total_cpu_time_sys_us.HashCS when total_cpu_time_sys_us.IsCS(hash, span) => Field.total_cpu_time_sys_us,
            total_net_bytes.HashCS when total_net_bytes.IsCS(hash, span) => Field.total_net_bytes,
            by_cpu_time_us.HashCS when by_cpu_time_us.IsCS(hash, span) => Field.by_cpu_time_us,
            by_net_bytes.HashCS when by_net_bytes.IsCS(hash, span) => Field.by_net_bytes,
            _ => Field.Unknown,
        };
    }

    [Benchmark]
    public Field SwitchOnLength()
    {
        ReadOnlySpan<byte> span = _bytes;
        var hash = FastHash.HashCS(span);
        return span.Length switch
        {
            key.Length when key.IsCS(hash, span) => Field.key,
            abc.Length when abc.IsCS(hash, span) => Field.abc,
            port.Length when port.IsCS(hash, span) => Field.port,
            test.Length when test.IsCS(hash, span) => Field.test,
            tracking_active.Length when tracking_active.IsCS(hash, span) => Field.tracking_active,
            sample_ratio.Length when sample_ratio.IsCS(hash, span) => Field.sample_ratio,
            selected_slots.Length when selected_slots.IsCS(hash, span) => Field.selected_slots,
            all_commands_all_slots_us.Length when all_commands_all_slots_us.IsCS(hash, span) => Field.all_commands_all_slots_us,
            all_commands_selected_slots_us.Length when all_commands_selected_slots_us.IsCS(hash, span) => Field.all_commands_selected_slots_us,
            sampled_command_selected_slots_us.Length when sampled_command_selected_slots_us.IsCS(hash, span) => Field.sampled_command_selected_slots_us,
            sampled_commands_selected_slots_us.Length when sampled_commands_selected_slots_us.IsCS(hash, span) => Field.sampled_commands_selected_slots_us,
            net_bytes_all_commands_all_slots.Length when net_bytes_all_commands_all_slots.IsCS(hash, span) => Field.net_bytes_all_commands_all_slots,
            net_bytes_all_commands_selected_slots.Length when net_bytes_all_commands_selected_slots.IsCS(hash, span) => Field.net_bytes_all_commands_selected_slots,
            net_bytes_sampled_commands_selected_slots.Length when net_bytes_sampled_commands_selected_slots.IsCS(hash, span) => Field.net_bytes_sampled_commands_selected_slots,
            collection_start_time_unix_ms.Length when collection_start_time_unix_ms.IsCS(hash, span) => Field.collection_start_time_unix_ms,
            collection_duration_ms.Length when collection_duration_ms.IsCS(hash, span) => Field.collection_duration_ms,
            collection_duration_us.Length when collection_duration_us.IsCS(hash, span) => Field.collection_duration_us,
            total_cpu_time_user_ms.Length when total_cpu_time_user_ms.IsCS(hash, span) => Field.total_cpu_time_user_ms,
            total_cpu_time_user_us.Length when total_cpu_time_user_us.IsCS(hash, span) => Field.total_cpu_time_user_us,
            total_cpu_time_sys_ms.Length when total_cpu_time_sys_ms.IsCS(hash, span) => Field.total_cpu_time_sys_ms,
            total_cpu_time_sys_us.Length when total_cpu_time_sys_us.IsCS(hash, span) => Field.total_cpu_time_sys_us,
            total_net_bytes.Length when total_net_bytes.IsCS(hash, span) => Field.total_net_bytes,
            by_cpu_time_us.Length when by_cpu_time_us.IsCS(hash, span) => Field.by_cpu_time_us,
            by_net_bytes.Length when by_net_bytes.IsCS(hash, span) => Field.by_net_bytes,
            _ => Field.Unknown,
        };
    }

    [Benchmark]
    public Field SwitchOnHash_CI()
    {
        ReadOnlySpan<byte> span = _bytes;
        var hash = FastHash.HashCI(span);
        return hash switch
        {
            key.HashCI when key.IsCI(hash, span) => Field.key,
            abc.HashCI when abc.IsCI(hash, span) => Field.abc,
            port.HashCI when port.IsCI(hash, span) => Field.port,
            test.HashCI when test.IsCI(hash, span) => Field.test,
            tracking_active.HashCI when tracking_active.IsCI(hash, span) => Field.tracking_active,
            sample_ratio.HashCI when sample_ratio.IsCI(hash, span) => Field.sample_ratio,
            selected_slots.HashCI when selected_slots.IsCI(hash, span) => Field.selected_slots,
            all_commands_all_slots_us.HashCI when all_commands_all_slots_us.IsCI(hash, span) => Field.all_commands_all_slots_us,
            all_commands_selected_slots_us.HashCI when all_commands_selected_slots_us.IsCI(hash, span) => Field.all_commands_selected_slots_us,
            sampled_command_selected_slots_us.HashCI when sampled_command_selected_slots_us.IsCI(hash, span) => Field.sampled_command_selected_slots_us,
            sampled_commands_selected_slots_us.HashCI when sampled_commands_selected_slots_us.IsCI(hash, span) => Field.sampled_commands_selected_slots_us,
            net_bytes_all_commands_all_slots.HashCI when net_bytes_all_commands_all_slots.IsCI(hash, span) => Field.net_bytes_all_commands_all_slots,
            net_bytes_all_commands_selected_slots.HashCI when net_bytes_all_commands_selected_slots.IsCI(hash, span) => Field.net_bytes_all_commands_selected_slots,
            net_bytes_sampled_commands_selected_slots.HashCI when net_bytes_sampled_commands_selected_slots.IsCI(hash, span) => Field.net_bytes_sampled_commands_selected_slots,
            collection_start_time_unix_ms.HashCI when collection_start_time_unix_ms.IsCI(hash, span) => Field.collection_start_time_unix_ms,
            collection_duration_ms.HashCI when collection_duration_ms.IsCI(hash, span) => Field.collection_duration_ms,
            collection_duration_us.HashCI when collection_duration_us.IsCI(hash, span) => Field.collection_duration_us,
            total_cpu_time_user_ms.HashCI when total_cpu_time_user_ms.IsCI(hash, span) => Field.total_cpu_time_user_ms,
            total_cpu_time_user_us.HashCI when total_cpu_time_user_us.IsCI(hash, span) => Field.total_cpu_time_user_us,
            total_cpu_time_sys_ms.HashCI when total_cpu_time_sys_ms.IsCI(hash, span) => Field.total_cpu_time_sys_ms,
            total_cpu_time_sys_us.HashCI when total_cpu_time_sys_us.IsCI(hash, span) => Field.total_cpu_time_sys_us,
            total_net_bytes.HashCI when total_net_bytes.IsCI(hash, span) => Field.total_net_bytes,
            by_cpu_time_us.HashCI when by_cpu_time_us.IsCI(hash, span) => Field.by_cpu_time_us,
            by_net_bytes.HashCI when by_net_bytes.IsCI(hash, span) => Field.by_net_bytes,
            _ => Field.Unknown,
        };
    }

    [Benchmark]
    public Field SwitchOnLength_CI()
    {
        ReadOnlySpan<byte> span = _bytes;
        var hash = FastHash.HashCI(span);
        return span.Length switch
        {
            key.Length when key.IsCI(hash, span) => Field.key,
            abc.Length when abc.IsCI(hash, span) => Field.abc,
            port.Length when port.IsCI(hash, span) => Field.port,
            test.Length when test.IsCI(hash, span) => Field.test,
            tracking_active.Length when tracking_active.IsCI(hash, span) => Field.tracking_active,
            sample_ratio.Length when sample_ratio.IsCI(hash, span) => Field.sample_ratio,
            selected_slots.Length when selected_slots.IsCI(hash, span) => Field.selected_slots,
            all_commands_all_slots_us.Length when all_commands_all_slots_us.IsCI(hash, span) => Field.all_commands_all_slots_us,
            all_commands_selected_slots_us.Length when all_commands_selected_slots_us.IsCI(hash, span) => Field.all_commands_selected_slots_us,
            sampled_command_selected_slots_us.Length when sampled_command_selected_slots_us.IsCI(hash, span) => Field.sampled_command_selected_slots_us,
            sampled_commands_selected_slots_us.Length when sampled_commands_selected_slots_us.IsCI(hash, span) => Field.sampled_commands_selected_slots_us,
            net_bytes_all_commands_all_slots.Length when net_bytes_all_commands_all_slots.IsCI(hash, span) => Field.net_bytes_all_commands_all_slots,
            net_bytes_all_commands_selected_slots.Length when net_bytes_all_commands_selected_slots.IsCI(hash, span) => Field.net_bytes_all_commands_selected_slots,
            net_bytes_sampled_commands_selected_slots.Length when net_bytes_sampled_commands_selected_slots.IsCI(hash, span) => Field.net_bytes_sampled_commands_selected_slots,
            collection_start_time_unix_ms.Length when collection_start_time_unix_ms.IsCI(hash, span) => Field.collection_start_time_unix_ms,
            collection_duration_ms.Length when collection_duration_ms.IsCI(hash, span) => Field.collection_duration_ms,
            collection_duration_us.Length when collection_duration_us.IsCI(hash, span) => Field.collection_duration_us,
            total_cpu_time_user_ms.Length when total_cpu_time_user_ms.IsCI(hash, span) => Field.total_cpu_time_user_ms,
            total_cpu_time_user_us.Length when total_cpu_time_user_us.IsCI(hash, span) => Field.total_cpu_time_user_us,
            total_cpu_time_sys_ms.Length when total_cpu_time_sys_ms.IsCI(hash, span) => Field.total_cpu_time_sys_ms,
            total_cpu_time_sys_us.Length when total_cpu_time_sys_us.IsCI(hash, span) => Field.total_cpu_time_sys_us,
            total_net_bytes.Length when total_net_bytes.IsCI(hash, span) => Field.total_net_bytes,
            by_cpu_time_us.Length when by_cpu_time_us.IsCI(hash, span) => Field.by_cpu_time_us,
            by_net_bytes.Length when by_net_bytes.IsCI(hash, span) => Field.by_net_bytes,
            _ => Field.Unknown,
        };
    }

    /*
     we're using raw output from the code-gen, because BDN kinda hates the tooling, because
     of the complex build pipe; this is left for reference only

    [FastHash] internal static partial class key { }
    [FastHash] internal static partial class abc { }
    [FastHash] internal static partial class port { }
    [FastHash] internal static partial class test { }
    [FastHash] internal static partial class tracking_active { }
    [FastHash] internal static partial class sample_ratio { }
    [FastHash] internal static partial class selected_slots { }
    [FastHash] internal static partial class all_commands_all_slots_us { }
    [FastHash] internal static partial class all_commands_selected_slots_us { }
    [FastHash] internal static partial class sampled_command_selected_slots_us { }
    [FastHash] internal static partial class sampled_commands_selected_slots_us { }
    [FastHash] internal static partial class net_bytes_all_commands_all_slots { }
    [FastHash] internal static partial class net_bytes_all_commands_selected_slots { }
    [FastHash] internal static partial class net_bytes_sampled_commands_selected_slots { }
    [FastHash] internal static partial class collection_start_time_unix_ms { }
    [FastHash] internal static partial class collection_duration_ms { }
    [FastHash] internal static partial class collection_duration_us { }
    [FastHash] internal static partial class total_cpu_time_user_ms { }
    [FastHash] internal static partial class total_cpu_time_user_us { }
    [FastHash] internal static partial class total_cpu_time_sys_ms { }
    [FastHash] internal static partial class total_cpu_time_sys_us { }
    [FastHash] internal static partial class total_net_bytes { }
    [FastHash] internal static partial class by_cpu_time_us { }
    [FastHash] internal static partial class by_net_bytes { }
    */

    static class key
    {
        public const int Length = 3;
        public const long HashCS = 7955819;
        public const long HashCI = 5850443;
        public static ReadOnlySpan<byte> U8 => "key"u8;
        public const string Text = "key";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.FastHash.HashCS(value) == HashCS || global::RESPite.FastHash.EqualsCI(value, U8));
    }
    static class abc
    {
        public const int Length = 3;
        public const long HashCS = 6513249;
        public const long HashCI = 4407873;
        public static ReadOnlySpan<byte> U8 => "abc"u8;
        public const string Text = "abc";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.FastHash.HashCS(value) == HashCS || global::RESPite.FastHash.EqualsCI(value, U8));
    }
    static class port
    {
        public const int Length = 4;
        public const long HashCS = 1953656688;
        public const long HashCI = 1414680400;
        public static ReadOnlySpan<byte> U8 => "port"u8;
        public const string Text = "port";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.FastHash.HashCS(value) == HashCS || global::RESPite.FastHash.EqualsCI(value, U8));
    }
    static class test
    {
        public const int Length = 4;
        public const long HashCS = 1953719668;
        public const long HashCI = 1414743380;
        public static ReadOnlySpan<byte> U8 => "test"u8;
        public const string Text = "test";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS & value.Length == Length;
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && (global::RESPite.FastHash.HashCS(value) == HashCS || global::RESPite.FastHash.EqualsCI(value, U8));
    }
    static class tracking_active
    {
        public const int Length = 15;
        public const long HashCS = 7453010343294497396;
        public const long HashCI = 5138124812476043860;
        public static ReadOnlySpan<byte> U8 => "tracking-active"u8;
        public const string Text = "tracking-active";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class sample_ratio
    {
        public const int Length = 12;
        public const long HashCS = 8227343610692854131;
        public const long HashCI = 5912458079874400595;
        public static ReadOnlySpan<byte> U8 => "sample-ratio"u8;
        public const string Text = "sample-ratio";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class selected_slots
    {
        public const int Length = 14;
        public const long HashCS = 7234316346692756851;
        public const long HashCI = 4919430815874303315;
        public static ReadOnlySpan<byte> U8 => "selected-slots"u8;
        public const string Text = "selected-slots";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class all_commands_all_slots_us
    {
        public const int Length = 25;
        public const long HashCS = 7885080994350132321;
        public const long HashCI = 5570195463531678785;
        public static ReadOnlySpan<byte> U8 => "all-commands-all-slots-us"u8;
        public const string Text = "all-commands-all-slots-us";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class all_commands_selected_slots_us
    {
        public const int Length = 30;
        public const long HashCS = 7885080994350132321;
        public const long HashCI = 5570195463531678785;
        public static ReadOnlySpan<byte> U8 => "all-commands-selected-slots-us"u8;
        public const string Text = "all-commands-selected-slots-us";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class sampled_command_selected_slots_us
    {
        public const int Length = 33;
        public const long HashCS = 3270850745794912627;
        public const long HashCI = 955965214976459091;
        public static ReadOnlySpan<byte> U8 => "sampled-command-selected-slots-us"u8;
        public const string Text = "sampled-command-selected-slots-us";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class sampled_commands_selected_slots_us
    {
        public const int Length = 34;
        public const long HashCS = 3270850745794912627;
        public const long HashCI = 955965214976459091;
        public static ReadOnlySpan<byte> U8 => "sampled-commands-selected-slots-us"u8;
        public const string Text = "sampled-commands-selected-slots-us";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class net_bytes_all_commands_all_slots
    {
        public const int Length = 32;
        public const long HashCS = 7310601557705516398;
        public const long HashCI = 4995716026887062862;
        public static ReadOnlySpan<byte> U8 => "net-bytes-all-commands-all-slots"u8;
        public const string Text = "net-bytes-all-commands-all-slots";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class net_bytes_all_commands_selected_slots
    {
        public const int Length = 37;
        public const long HashCS = 7310601557705516398;
        public const long HashCI = 4995716026887062862;
        public static ReadOnlySpan<byte> U8 => "net-bytes-all-commands-selected-slots"u8;
        public const string Text = "net-bytes-all-commands-selected-slots";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class net_bytes_sampled_commands_selected_slots
    {
        public const int Length = 41;
        public const long HashCS = 7310601557705516398;
        public const long HashCI = 4995716026887062862;
        public static ReadOnlySpan<byte> U8 => "net-bytes-sampled-commands-selected-slots"u8;
        public const string Text = "net-bytes-sampled-commands-selected-slots";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class collection_start_time_unix_ms
    {
        public const int Length = 29;
        public const long HashCS = 7598807758542761827;
        public const long HashCI = 5283922227724308291;
        public static ReadOnlySpan<byte> U8 => "collection-start-time-unix-ms"u8;
        public const string Text = "collection-start-time-unix-ms";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class collection_duration_ms
    {
        public const int Length = 22;
        public const long HashCS = 7598807758542761827;
        public const long HashCI = 5283922227724308291;
        public static ReadOnlySpan<byte> U8 => "collection-duration-ms"u8;
        public const string Text = "collection-duration-ms";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class collection_duration_us
    {
        public const int Length = 22;
        public const long HashCS = 7598807758542761827;
        public const long HashCI = 5283922227724308291;
        public static ReadOnlySpan<byte> U8 => "collection-duration-us"u8;
        public const string Text = "collection-duration-us";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class total_cpu_time_user_ms
    {
        public const int Length = 22;
        public const long HashCS = 8098366498457022324;
        public const long HashCI = 5783480967638568788;
        public static ReadOnlySpan<byte> U8 => "total-cpu-time-user-ms"u8;
        public const string Text = "total-cpu-time-user-ms";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class total_cpu_time_user_us
    {
        public const int Length = 22;
        public const long HashCS = 8098366498457022324;
        public const long HashCI = 5783480967638568788;
        public static ReadOnlySpan<byte> U8 => "total-cpu-time-user-us"u8;
        public const string Text = "total-cpu-time-user-us";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class total_cpu_time_sys_ms
    {
        public const int Length = 21;
        public const long HashCS = 8098366498457022324;
        public const long HashCI = 5783480967638568788;
        public static ReadOnlySpan<byte> U8 => "total-cpu-time-sys-ms"u8;
        public const string Text = "total-cpu-time-sys-ms";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class total_cpu_time_sys_us
    {
        public const int Length = 21;
        public const long HashCS = 8098366498457022324;
        public const long HashCI = 5783480967638568788;
        public static ReadOnlySpan<byte> U8 => "total-cpu-time-sys-us"u8;
        public const string Text = "total-cpu-time-sys-us";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class total_net_bytes
    {
        public const int Length = 15;
        public const long HashCS = 7308829188783632244;
        public const long HashCI = 4993943657965178708;
        public static ReadOnlySpan<byte> U8 => "total-net-bytes"u8;
        public const string Text = "total-net-bytes";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class by_cpu_time_us
    {
        public const int Length = 14;
        public const long HashCS = 8371476407912331618;
        public const long HashCI = 6056590877093878082;
        public static ReadOnlySpan<byte> U8 => "by-cpu-time-us"u8;
        public const string Text = "by-cpu-time-us";
        public static bool IsCS(long hash, ReadOnlySpan<byte> value) => hash == HashCS && value.SequenceEqual(U8);
        public static bool IsCI(long hash, ReadOnlySpan<byte> value) => (hash == HashCI & value.Length == Length) && global::RESPite.FastHash.EqualsCI(value, U8);
    }
    static class by_net_bytes
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
