using BenchmarkDotNet.Attributes;
using System.Net;
using RESPite.Transports;
using RESPite.Resp;
using StackExchange.Redis;
namespace BasicTest
{
    [Config(typeof(CustomConfig))]
    public class RespiteBenchmarks
    {
        private IRequestResponseTransport _respite;
        private ConnectionMultiplexer _muxer;
        private IDatabase _db;
        [GlobalSetup]
        public void Setup()
        {
            var ep = new IPEndPoint(IPAddress.Loopback, 6379);

            _respite = ep.CreateTransport().RequestResponse(RespFrameScanner.Default);
            _muxer = ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { ep }
            });
            _db = _muxer.GetDatabase();
        }
        private const string Key = "mykey";
        [Benchmark(Baseline = true)]
        public long SERedis() => _db.StringIncrement(Key);
        [Benchmark]
        public long RESpite() => _respite.Send(Key, RespWriters.Incr, RespReaders.Int32);
    }
}
