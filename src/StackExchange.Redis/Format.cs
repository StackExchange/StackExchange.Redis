using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Text;
using System.Diagnostics.CodeAnalysis;

#if UNIX_SOCKET
using System.Net.Sockets;
#endif

namespace StackExchange.Redis
{
    internal static class Format
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
        public static int ParseInt32(ReadOnlySpan<char> s) => int.Parse(s, NumberStyles.Integer, NumberFormatInfo.InvariantInfo);
        public static bool TryParseInt32(ReadOnlySpan<char> s, out int value) => int.TryParse(s, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out value);
#endif

        public static int ParseInt32(string s) => int.Parse(s, NumberStyles.Integer, NumberFormatInfo.InvariantInfo);

        public static long ParseInt64(string s) => long.Parse(s, NumberStyles.Integer, NumberFormatInfo.InvariantInfo);

        public static string ToString(int value) => value.ToString(NumberFormatInfo.InvariantInfo);

        public static bool TryParseBoolean(string s, out bool value)
        {
            if (bool.TryParse(s, out value)) return true;

            if (s == "1" || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "on", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }
            if (s == "0" || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "off", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }
            value = false;
            return false;
        }

        public static bool TryParseInt32(string s, out int value) =>
            int.TryParse(s, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out value);

        internal static EndPoint ParseEndPoint(string host, int port)
        {
            if (IPAddress.TryParse(host, out IPAddress? ip)) return new IPEndPoint(ip, port);
            return new DnsEndPoint(host, port);
        }

        internal static bool TryParseEndPoint(string host, string? port, [NotNullWhen(true)] out EndPoint? endpoint)
        {
            if (!host.IsNullOrEmpty() && !port.IsNullOrEmpty() && TryParseInt32(port, out int i))
            {
                endpoint = ParseEndPoint(host, i);
                return true;
            }
            endpoint = null;
            return false;
        }

        internal static string ToString(long value) => value.ToString(NumberFormatInfo.InvariantInfo);

        internal static string ToString(ulong value) => value.ToString(NumberFormatInfo.InvariantInfo);

        internal static string ToString(double value)
        {
            if (double.IsInfinity(value))
            {
                if (double.IsPositiveInfinity(value)) return "+inf";
                if (double.IsNegativeInfinity(value)) return "-inf";
            }
            return value.ToString("G17", NumberFormatInfo.InvariantInfo);
        }

        [return: NotNullIfNotNull("value")]
        internal static string? ToString(object? value) => value switch
        {
            null => "",
            long l => ToString(l),
            int i => ToString(i),
            float f => ToString(f),
            double d => ToString(d),
            EndPoint e => ToString(e),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

        internal static string ToString(EndPoint? endpoint)
        {
            switch (endpoint)
            {
                case DnsEndPoint dns:
                    if (dns.Port == 0) return dns.Host;
                    return dns.Host + ":" + Format.ToString(dns.Port);
                case IPEndPoint ip:
                    if (ip.Port == 0) return ip.Address.ToString();
                    return ip.Address + ":" + Format.ToString(ip.Port);
#if UNIX_SOCKET
                case UnixDomainSocketEndPoint uds:
                    return "!" + uds.ToString();
#endif
                default:
                    return endpoint?.ToString() ?? "";
            }
        }

        internal static string ToStringHostOnly(EndPoint endpoint) =>
            endpoint switch
            {
                DnsEndPoint dns => dns.Host,
                IPEndPoint ip => ip.Address.ToString(),
                _ => ""
            };

        internal static bool TryGetHostPort(EndPoint? endpoint, [NotNullWhen(true)] out string? host, [NotNullWhen(true)] out int? port)
        {
            if (endpoint is not null)
            {
                if (endpoint is IPEndPoint ip)
                {
                    host = ip.Address.ToString();
                    port = ip.Port;
                    return true;
                }
                if (endpoint is DnsEndPoint dns)
                {
                    host = dns.Host;
                    port = dns.Port;
                    return true;
                }
            }
            host = null;
            port = null;
            return false;
        }

        internal static bool TryParseDouble(string? s, out double value)
        {
            if (s is null)
            {
                value = 0;
                return false;
            }
            switch (s.Length)
            {
                case 0:
                    value = 0;
                    return false;
                // single-digits
                case 1 when s[0] >= '0' && s[0] <= '9':
                    value = s[0] - '0';
                    return true;
                // RESP3 spec demands inf/nan handling
                case 3 when CaseInsensitiveASCIIEqual("inf", s):
                    value = double.PositiveInfinity;
                    return true;
                case 3 when CaseInsensitiveASCIIEqual("nan", s):
                    value = double.NaN;
                    return true;
                case 4 when CaseInsensitiveASCIIEqual("+inf", s):
                    value = double.PositiveInfinity;
                    return true;
                case 4 when CaseInsensitiveASCIIEqual("-inf", s):
                    value = double.NegativeInfinity;
                    return true;
                case 4 when CaseInsensitiveASCIIEqual("+nan", s):
                case 4 when CaseInsensitiveASCIIEqual("-nan", s):
                    value = double.NaN;
                    return true;
            }
            return double.TryParse(s, NumberStyles.Any, NumberFormatInfo.InvariantInfo, out value);
        }

        internal static bool TryParseUInt64(string s, out ulong value) =>
            ulong.TryParse(s, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out value);

        internal static bool TryParseUInt64(ReadOnlySpan<byte> s, out ulong value) =>
            Utf8Parser.TryParse(s, out value, out int bytes, standardFormat: 'D') & bytes == s.Length;

        internal static bool TryParseInt64(ReadOnlySpan<byte> s, out long value) =>
            Utf8Parser.TryParse(s, out value, out int bytes, standardFormat: 'D') & bytes == s.Length;

        internal static bool CouldBeInteger(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > Format.MaxInt64TextLen) return false;
            bool isSigned = s[0] == '-';
            for (int i = isSigned ? 1 : 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' | c > '9') return false;
            }
            return true;
        }
        internal static bool CouldBeInteger(ReadOnlySpan<byte> s)
        {
            if (s.IsEmpty | s.Length > Format.MaxInt64TextLen) return false;
            bool isSigned = s[0] == '-';
            for (int i = isSigned ? 1 : 0; i < s.Length; i++)
            {
                byte c = s[i];
                if (c < (byte)'0' | c > (byte)'9') return false;
            }
            return true;
        }

        internal static bool TryParseInt64(string s, out long value) =>
            long.TryParse(s, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out value);

        internal static bool TryParseDouble(ReadOnlySpan<byte> s, out double value)
        {
            switch (s.Length)
            {
                case 0:
                    value = 0;
                    return false;
                // single-digits
                case 1 when s[0] >= '0' && s[0] <= '9':
                    value = s[0] - '0';
                    return true;
                // RESP3 spec demands inf/nan handling
                case 3 when CaseInsensitiveASCIIEqual("inf", s):
                    value = double.PositiveInfinity;
                    return true;
                case 3 when CaseInsensitiveASCIIEqual("nan", s):
                    value = double.NaN;
                    return true;
                case 4 when CaseInsensitiveASCIIEqual("+inf", s):
                    value = double.PositiveInfinity;
                    return true;
                case 4 when CaseInsensitiveASCIIEqual("-inf", s):
                    value = double.NegativeInfinity;
                    return true;
                case 4 when CaseInsensitiveASCIIEqual("+nan", s):
                case 4 when CaseInsensitiveASCIIEqual("-nan", s):
                    value = double.NaN;
                    return true;
            }
            return Utf8Parser.TryParse(s, out value, out int bytes) & bytes == s.Length;
        }

        private static bool CaseInsensitiveASCIIEqual(string xLowerCase, string y)
            => string.Equals(xLowerCase, y, StringComparison.OrdinalIgnoreCase);

        private static bool CaseInsensitiveASCIIEqual(string xLowerCase, ReadOnlySpan<byte> y)
        {
            if (y.Length != xLowerCase.Length) return false;
            for (int i = 0; i < y.Length; i++)
            {
                if (char.ToLower((char)y[i]) != xLowerCase[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// <para>
        /// Adapted from IPEndPointParser in Microsoft.AspNetCore
        /// Link: <see href="https://github.com/aspnet/BasicMiddleware/blob/f320511b63da35571e890d53f3906c7761cd00a1/src/Microsoft.AspNetCore.HttpOverrides/Internal/IPEndPointParser.cs#L8"/>
        /// </para>
        /// <para>
        /// Copyright (c) .NET Foundation. All rights reserved.
        /// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
        /// </para>
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">If Unix sockets are attempted but not supported.</exception>
        internal static bool TryParseEndPoint(string? addressWithPort, [NotNullWhen(true)] out EndPoint? endpoint)
        {
            string addressPart;
            string? portPart = null;
            if (addressWithPort.IsNullOrEmpty())
            {
                endpoint = null;
                return false;
            }

            if (addressWithPort[0]=='!')
            {
                if (addressWithPort.Length == 1)
                {
                    endpoint = null;
                    return false;
                }

#if UNIX_SOCKET
                endpoint = new UnixDomainSocketEndPoint(addressWithPort.Substring(1));
                return true;
#else
                throw new PlatformNotSupportedException("Unix domain sockets require .NET Core 3 or above");
#endif
            }
            var lastColonIndex = addressWithPort.LastIndexOf(':');
            if (lastColonIndex > 0)
            {
                // IPv4 with port or IPv6
                var closingIndex = addressWithPort.LastIndexOf(']');
                if (closingIndex > 0)
                {
                    // IPv6 with brackets
                    addressPart = addressWithPort.Substring(1, closingIndex - 1);
                    if (closingIndex < lastColonIndex)
                    {
                        // IPv6 with port [::1]:80
                        portPart = addressWithPort.Substring(lastColonIndex + 1);
                    }
                }
                else
                {
                    // IPv6 without port or IPv4
                    var firstColonIndex = addressWithPort.IndexOf(':');
                    if (firstColonIndex != lastColonIndex)
                    {
                        // IPv6 ::1
                        addressPart = addressWithPort;
                    }
                    else
                    {
                        // IPv4 with port 127.0.0.1:123
                        addressPart = addressWithPort.Substring(0, firstColonIndex);
                        portPart = addressWithPort.Substring(firstColonIndex + 1);
                    }
                }
            }
            else
            {
                // IPv4 without port
                addressPart = addressWithPort;
            }

            int? port = 0;
            if (portPart != null)
            {
                if (TryParseInt32(portPart, out var portVal))
                {
                    port = portVal;
                }
                else
                {
                    // Invalid port, return
                    endpoint = null;
                    return false;
                }
            }

            if (IPAddress.TryParse(addressPart, out IPAddress? address))
            {
                endpoint = new IPEndPoint(address, port ?? 0);
                return true;
            }
            else
            {
                endpoint = new DnsEndPoint(addressPart, port ?? 0);
                return true;
            }
        }

        internal static string GetString(ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment) return GetString(buffer.First.Span);

            var arr = ArrayPool<byte>.Shared.Rent(checked((int)buffer.Length));
            var span = new Span<byte>(arr, 0, (int)buffer.Length);
            buffer.CopyTo(span);
            string s = GetString(span);
            ArrayPool<byte>.Shared.Return(arr);
            return s;
        }

        internal static unsafe string GetString(ReadOnlySpan<byte> span)
        {
            if (span.IsEmpty) return "";
#if NETCOREAPP3_1_OR_GREATER
            return Encoding.UTF8.GetString(span);
#else
            fixed (byte* ptr = span)
            {
                return Encoding.UTF8.GetString(ptr, span.Length);
            }
#endif
        }

        [DoesNotReturn]
        private static void ThrowFormatFailed() => throw new InvalidOperationException("TryFormat failed");

        internal const int
            MaxInt32TextLen = 11, // -2,147,483,648 (not including the commas)
            MaxInt64TextLen = 20; // -9,223,372,036,854,775,808 (not including the commas)

        internal static int MeasureDouble(double value)
        {
            if (double.IsInfinity(value)) return 4; // +inf / -inf
            var s = value.ToString("G17", NumberFormatInfo.InvariantInfo); // this looks inefficient, but is how Utf8Formatter works too, just: more direct
            return s.Length;
        }

        internal static int FormatDouble(double value, Span<byte> destination)
        {
            if (double.IsInfinity(value))
            {
                if (double.IsPositiveInfinity(value))
                {
                    if (!"+inf"u8.TryCopyTo(destination)) ThrowFormatFailed();
                }
                else
                {
                    if (!"-inf"u8.TryCopyTo(destination)) ThrowFormatFailed();
                }
                return 4;
            }
            var s = value.ToString("G17", NumberFormatInfo.InvariantInfo); // this looks inefficient, but is how Utf8Formatter works too, just: more direct
            if (s.Length > destination.Length) ThrowFormatFailed();

            var chars = s.AsSpan();
            for (int i = 0; i < chars.Length; i++)
            {
                destination[i] = (byte)chars[i];
            }
            return chars.Length;
        }

        internal static int MeasureInt64(long value)
        {
            Span<byte> valueSpan = stackalloc byte[MaxInt64TextLen];
            return FormatInt64(value, valueSpan);
        }

        internal static int FormatInt64(long value, Span<byte> destination)
        {
            if (!Utf8Formatter.TryFormat(value, destination, out var len))
                ThrowFormatFailed();
            return len;
        }

        internal static int MeasureUInt64(ulong value)
        {
            Span<byte> valueSpan = stackalloc byte[MaxInt64TextLen];
            return FormatUInt64(value, valueSpan);
        }

        internal static int FormatUInt64(ulong value, Span<byte> destination)
        {
            if (!Utf8Formatter.TryFormat(value, destination, out var len))
                ThrowFormatFailed();
            return len;
        }

        internal static int FormatInt32(int value, Span<byte> destination)
        {
            if (!Utf8Formatter.TryFormat(value, destination, out var len))
                ThrowFormatFailed();
            return len;
        }

        internal static bool TryParseVersion(ReadOnlySpan<char> input, [NotNullWhen(true)] out Version? version)
        {
#if NETCOREAPP3_1_OR_GREATER
            if (Version.TryParse(input, out version)) return true;
            // allow major-only (Version doesn't do this, because... reasons?)
            if (TryParseInt32(input, out int i32))
            {
                version = new(i32, 0);
                return true;
            }
            version = null;
            return false;
#else
            if (input.IsEmpty)
            {
                version = null;
                return false;
            }
            unsafe
            {
                fixed (char* ptr = input)
                {
                    string s = new(ptr, 0, input.Length);
                    return TryParseVersion(s, out version);
                }
            }
#endif
        }

        internal static bool TryParseVersion(string? input, [NotNullWhen(true)] out Version? version)
        {
            if (input is not null)
            {
                if (Version.TryParse(input, out version)) return true;
                // allow major-only (Version doesn't do this, because... reasons?)
                if (TryParseInt32(input, out int i32))
                {
                    version = new(i32, 0);
                    return true;
                }
            }
            version = null;
            return false;
        }
    }
}
