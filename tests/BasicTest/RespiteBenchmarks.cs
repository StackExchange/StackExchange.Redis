using BenchmarkDotNet.Attributes;
using System.Net;
using RESPite.Transports;
using RESPite.Resp;
using StackExchange.Redis;
using System.Threading.Tasks;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using RESPite;
namespace BasicTest;

[Config(typeof(CustomConfig))]
public class RespiteBenchmarks
{
    private IRequestResponseTransport _respite;
    private ConnectionMultiplexer _muxer;
    private IDatabase _db;
    private readonly byte[] _blob = new byte[8 * 1024];
    [GlobalSetup]
    public void Setup()
    {
        var ep = new IPEndPoint(IPAddress.Loopback, 3278);

        _respite = ep.CreateTransport().RequestResponse(RespFrameScanner.Default).WithSemaphoreSlimSynchronization();
        _muxer = ConnectionMultiplexer.Connect(new ConfigurationOptions
        {
            EndPoints = { ep }
        });
        new Random().NextBytes(_blob);
        _db = _muxer.GetDatabase();
        _db.KeyDelete(Key);
    }
    private const string Key = "mykey";
    [Benchmark, Category("Sequential")]
    public void SERedis_Set() => _db.StringSet(Key, _blob);

    [Benchmark, Category("Sequential")]
    public Task SERedis_Set_Async() => _db.StringSetAsync(Key, _blob);

    [Benchmark, Category("Sequential")]
    public void RESpite_Set() => _respite.Send((Key, _blob), RespWriters.Set, RespReaders.OK);

    [Benchmark, Category("Sequential")]
    public ValueTask<Empty> RESpite_Set_Async() => _respite.SendAsync((Key, _blob), RespWriters.Set, RespReaders.OK);
}
