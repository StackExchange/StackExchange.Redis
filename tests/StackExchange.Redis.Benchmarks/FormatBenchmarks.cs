/*
using System;
using System.Net;
using BenchmarkDotNet.Attributes;

namespace StackExchange.Redis.Benchmarks
{
    [Config(typeof(CustomConfig))]
    public class FormatBenchmarks
    {
        [GlobalSetup]
        public void Setup() { }

        [Benchmark]
        [Arguments("64")]
        [Arguments("-1")]
        [Arguments("0")]
        [Arguments("123442")]
        public long ParseInt64(string s) => Format.ParseInt64(s);

        [Benchmark]
        [Arguments("64")]
        [Arguments("-1")]
        [Arguments("0")]
        [Arguments("123442")]
        public long ParseInt32(string s) => Format.ParseInt32(s);

        [Benchmark]
        [Arguments("64")]
        [Arguments("-1")]
        [Arguments("0")]
        [Arguments("123442")]
        [Arguments("-inf")]
        [Arguments("nan")]
        public double ParseDouble(string s) => Format.TryParseDouble(s, out var val) ? val : double.NaN;

        private byte[] buffer = new byte[128];

        [Benchmark]
        [Arguments(64D)]
        [Arguments(-1D)]
        [Arguments(0D)]
        [Arguments(123442D)]
        [Arguments(double.NegativeInfinity)]
        [Arguments(double.NaN)]
        public int FormatDouble(double value) => Format.FormatDouble(value, buffer.AsSpan());

        [Benchmark]
        [Arguments("host.com", -1)]
        [Arguments("host.com", 0)]
        [Arguments("host.com", 65345)]
        public EndPoint ParseEndPoint(string host, int port) => Format.ParseEndPoint(host, port);
    }
}
*/
