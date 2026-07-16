using RESPite;

namespace StackExchange.Redis;

internal partial class ResultProcessor
{
    internal partial class Literals
    {
#pragma warning disable CS8981, SA1300, SA1134 // forgive naming etc
        // ReSharper disable InconsistentNaming
        // Result processor literals
        [AsciiHash]
        internal static partial class OK
        {
            public static readonly AsciiHash Hash = new(U8);
        }

        [AsciiHash]
        internal static partial class PONG
        {
            public static readonly AsciiHash Hash = new(U8);
        }

        [AsciiHash("Background saving started")]
        internal static partial class background_saving_started
        {
            public static readonly AsciiHash Hash = new(U8);
        }

        [AsciiHash("Background append only file rewriting started")]
        internal static partial class background_aof_rewriting_started
        {
            public static readonly AsciiHash Hash = new(U8);
        }
        // ReSharper restore InconsistentNaming
#pragma warning restore CS8981, SA1300, SA1134 // forgive naming etc
    }
}
