using BenchmarkDotNet.Attributes;
using System.Net;
using RESPite.Transports;
using RESPite.Resp;
using StackExchange.Redis;
using System.Threading.Tasks;
namespace BasicTest;

[Config(typeof(CustomConfig))]
public class RespiteBenchmarks
{
    private IRequestResponseTransport _respite;
    private ConnectionMultiplexer _muxer;
    private IDatabase _db;
    [GlobalSetup]
    public void Setup()
    {
        var ep = new IPEndPoint(IPAddress.Loopback, 3278);

        _respite = ep.CreateTransport().RequestResponse(RespFrameScanner.Default);
        _muxer = ConnectionMultiplexer.Connect(new ConfigurationOptions
        {
            EndPoints = { ep }
        });
        _db = _muxer.GetDatabase();
    }
    private const string Key = "mykey";
    [Benchmark]
    public long SERedis() => _db.StringIncrement(Key);
    [Benchmark]
    public Task<long> SERedisAsync() => _db.StringIncrementAsync(Key);
    [Benchmark]
    public int RESpite() => _respite.Send(Key, RespWriters.Incr, RespReaders.Int32);
    [Benchmark]
    public ValueTask<int> RESpiteAsync() => _respite.SendAsync(Key, RespWriters.Incr, RespReaders.Int32);
}
