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
using RESPite.Messages;
using System.Buffers;
namespace BasicTest;

[Config(typeof(CustomConfig))]
public class RESPiteBenchmarks
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

    //[Benchmark, Category("Sequential")]
    public void SERedis_Set() => _db.StringSet(Key, _blob);

    //[Benchmark, Category("Sequential")]
    public Task SERedis_Set_Async() => _db.StringSetAsync(Key, _blob);

    [Benchmark, Category("Sequential")]
    public void RESPite_Set() => _respite.Send((Key, _blob), RespWriters.Set, RespReaders.OK);

    [Benchmark, Category("Sequential")]
    public ValueTask<Empty> RESPite_Set_Async() => _respite.SendAsync((Key, _blob), RespWriters.Set, RespReaders.OK);

    // the idea of this "get" test is to fetch the bytes without
    // materializing a BLOB, i.e. idealized case; we just need to
    // demonstrate that we have access to the BLOB to work with it
    [Benchmark, Category("Sequential")]
    public int SERedis_Get()
    {
        using var lease = _db.StringGetLease(Key);
        return lease.Length;
    }
    [Benchmark, Category("Sequential")]
    public async Task<int> SERedis_Get_Async()
    {
        using var lease = await _db.StringGetLeaseAsync(Key);
        return lease.Length;
    }

    [Benchmark, Category("Sequential")]
    public int RESPite_Get() => _respite.Send(Key, RespWriters.Get, CustomHandler.Instance);

    [Benchmark, Category("Sequential")]
    public ValueTask<int> RESPite_Get_Async() => _respite.SendAsync(Key, RespWriters.Get, CustomHandler.Instance);

    private class CustomHandler : RespReaderBase<int>
    {
        public static CustomHandler Instance { get; } = new();
        public override int Read(ref RespReader reader) => reader.IsNull ? -1 : reader.ScalarLength;
    }
}
