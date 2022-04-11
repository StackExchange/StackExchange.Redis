using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
#nullable enable

namespace StackExchange.Redis
{
    internal static class Logging
    {
        /// <summary>
        /// Log a <see cref="Microsoft.Extensions.Logging.LogLevel.Debug"/> event.
        /// </summary>
        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug<TState>(this ILogger? logger, TState state, Func<TState, Exception?, string> formatter, Exception? exception = null)
            => logger?.LogCore(LogLevel.Debug, state, exception, formatter);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogCore<TState>(this ILogger logger, LogLevel level, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                logger.Log<TState>(level, default, state, exception, formatter);
            }
            catch { } // let's just never throw while logging, eh?
        }

        /// <summary>
        /// Log a <see cref="Microsoft.Extensions.Logging.LogLevel.Debug"/> event.
        /// </summary>
        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(this ILogger? logger, string message)
            => logger?.LogCore(LogLevel.Debug, message, null, static (state, _) => state);

        /// <summary>
        /// Log a <see cref="Microsoft.Extensions.Logging.LogLevel.Error"/> event.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(this ILogger? logger, Exception exception, [CallerMemberName] string caller = "")
            => logger?.LogCore(LogLevel.Error, caller, exception, static (state, ex) => $"[{state}]: {ex!.Message}");

        /// <summary>
        /// Gets the buffer value as hexadecimal
        /// </summary>
        public static string ToHex(this Memory<byte> data) => ToHex((ReadOnlyMemory<byte>)data);
        /// <summary>
        /// Gets the buffer value as hexadecimal
        /// </summary>
        public static string ToHex(this ReadOnlyMemory<byte> data)
        {
#if NET5_0_OR_GREATER
            return Convert.ToHexString(data.Span);
#else
            if (MemoryMarshal.TryGetArray<byte>(data, out var segment) && segment.Array is not null)
            {   // this is a debug API; not concerned about bad overheads here
                return BitConverter.ToString(segment.Array, segment.Offset, segment.Count).Replace("-", "");
            }
            return "n/a";
#endif
        }
    }
}
