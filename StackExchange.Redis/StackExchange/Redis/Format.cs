﻿using System;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace StackExchange.Redis
{
    internal static class Format
    {
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

        public static bool TryParseInt32(string s, out int value)
        {
            return int.TryParse(s, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out value);
        }

        internal static EndPoint ParseEndPoint(string host, int port)
        {
            if (IPAddress.TryParse(host, out IPAddress ip)) return new IPEndPoint(ip, port);
            return new DnsEndPoint(host, port);
        }

        internal static EndPoint TryParseEndPoint(string host, string port)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port)) return null;
            return TryParseInt32(port, out int i) ? ParseEndPoint(host, i) : null;
        }

        internal static string ToString(long value) => value.ToString(NumberFormatInfo.InvariantInfo);

        internal static string ToString(double value)
        {
            if (double.IsInfinity(value))
            {
                if (double.IsPositiveInfinity(value)) return "+inf";
                if (double.IsNegativeInfinity(value)) return "-inf";
            }
            return value.ToString("G17", NumberFormatInfo.InvariantInfo);
        }

        internal static string ToString(object value)
        {
            if (value == null) return "";
            if (value is long l) return ToString(l);
            if (value is int i) return ToString(i);
            if (value is float f) return ToString(f);
            if (value is double d) return ToString(d);
            if (value is EndPoint e) return ToString(e);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        internal static string ToString(EndPoint endpoint)
        {
            if (endpoint is DnsEndPoint dns)
            {
                if (dns.Port == 0) return dns.Host;
                return dns.Host + ":" + Format.ToString(dns.Port);
            }
            if (endpoint is IPEndPoint ip)
            {
                if (ip.Port == 0) return ip.Address.ToString();
                return ip.Address + ":" + Format.ToString(ip.Port);
            }
            return endpoint?.ToString() ?? "";
        }

        internal static string ToStringHostOnly(EndPoint endpoint)
        {
            if (endpoint is DnsEndPoint dns)
            {
                return dns.Host;
            }
            if (endpoint is IPEndPoint ip)
            {
                return ip.Address.ToString();
            }
            return "";
        }

        internal static bool TryGetHostPort(EndPoint endpoint, out string host, out int port)
        {
            if (endpoint != null)
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
            port = 0;
            return false;
        }

        internal static bool TryParseDouble(string s, out double value)
        {
            if (string.IsNullOrEmpty(s))
            {
                value = 0;
                return false;
            }
            if (s.Length == 1 && s[0] >= '0' && s[0] <= '9')
            {
                value = (int)(s[0] - '0');
                return true;
            }
            // need to handle these
            if (string.Equals("+inf", s, StringComparison.OrdinalIgnoreCase) || string.Equals("inf", s, StringComparison.OrdinalIgnoreCase))
            {
                value = double.PositiveInfinity;
                return true;
            }
            if (string.Equals("-inf", s, StringComparison.OrdinalIgnoreCase))
            {
                value = double.NegativeInfinity;
                return true;
            }
            return double.TryParse(s, NumberStyles.Any, NumberFormatInfo.InvariantInfo, out value);
        }

        internal static bool TryParseDouble(ReadOnlySpan<byte> s, out double value)
        {
            if (s.IsEmpty)
            {
                value = 0;
                return false;
            }
            if (s.Length == 1 && s[0] >= '0' && s[0] <= '9')
            {
                value = (int)(s[0] - '0');
                return true;
            }
            // need to handle these
            if (CaseInsensitiveASCIIEqual("+inf", s) || CaseInsensitiveASCIIEqual("inf", s))
            {
                value = double.PositiveInfinity;
                return true;
            }
            if (CaseInsensitiveASCIIEqual("-inf", s))
            {
                value = double.NegativeInfinity;
                return true;
            }
            var ss = DecodeUtf8(s);
            return double.TryParse(ss, NumberStyles.Any, NumberFormatInfo.InvariantInfo, out value);
        }
        internal static unsafe string DecodeUtf8(ReadOnlySpan<byte> span)
        {
            if (span.IsEmpty) return "";
            fixed(byte* ptr = &MemoryMarshal.GetReference(span))
            {
                return Encoding.UTF8.GetString(ptr, span.Length);
            }
        }
        private static bool CaseInsensitiveASCIIEqual(string xLowerCase, ReadOnlySpan<byte> y)
        {
            if (y.Length != xLowerCase.Length) return false;
            for(int i = 0; i < y.Length; i++)
            {
                if (char.ToLower((char)y[i]) != xLowerCase[i]) return false;
            }
            return true;
        }

        internal static EndPoint TryParseEndPoint(string addressWithPort)
        {
            // Adapted from IPEndPointParser in Microsoft.AspNetCore
            // Link: https://github.com/aspnet/BasicMiddleware/blob/f320511b63da35571e890d53f3906c7761cd00a1/src/Microsoft.AspNetCore.HttpOverrides/Internal/IPEndPointParser.cs#L8
            // Copyright (c) .NET Foundation. All rights reserved.
            // Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
            string addressPart = null;
            string portPart = null;
            if (string.IsNullOrEmpty(addressWithPort)) return null;

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
                if (int.TryParse(portPart, out var portVal))
                {
                    port = portVal;
                }
                else
                {
                    // Invalid port, return
                    return null;
                }
            }

            if (IPAddress.TryParse(addressPart, out IPAddress address))
            {
                return new IPEndPoint(address, port ?? 0);
            }
            return new DnsEndPoint(addressPart, port ?? 0);
        }
    }
}
