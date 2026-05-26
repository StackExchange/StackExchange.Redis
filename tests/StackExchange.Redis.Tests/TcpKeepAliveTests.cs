using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class TcpKeepAliveTests(ITestOutputHelper log, TcpTestFixture fixture) : IClassFixture<TcpTestFixture>
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Roundtrip(bool ip, bool keepAlive)
    {
        #if NETFRAMEWORK
        Assert.SkipWhen(
            !ip && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Mono has glitches with DNS endpoints");
        #endif
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        EndPoint ep = ip ? fixture.IP : fixture.Dns;
        log.WriteLine($"Connecting to {Format.ToString(ep)}, {ep.AddressFamily}, keepAlive: {keepAlive}");
        if (keepAlive)
        {
            Assert.SkipUnless(SocketManager.TryEnableTcpKeepAlive(client, ep), "keep-alive not supported");
        }

        await client.ConnectAsync(ep);

        byte[] buffer = new byte[4];
        int i = random.Next(int.MinValue, int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(buffer, i);
        client.Send(buffer, 0, 4, SocketFlags.None);
        Array.Clear(buffer, 0, buffer.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(buffer));
        int bytesRead, count = 0;
        while (count < buffer.Length &&
               (bytesRead = client.Receive(buffer, count, buffer.Length - count, SocketFlags.None)) > 0)
        {
            count += bytesRead;
        }
        if (count != buffer.Length) throw new EndOfStreamException();
        Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(buffer));
    }
    private static readonly Random random = new();
}

public class TcpTestFixture : IDisposable
{
    private TcpListener server;
    private CancellationTokenSource cts = new();
    public IPEndPoint IP { get; }
    public DnsEndPoint Dns { get; }

    public TcpTestFixture()
    {
        int port = 18000;
#if NET10_OR_GREATER
        port += 1;
#elif NET8_0_OR_GREATER
        port += 2;
#elif NET6_0_OR_GREATER
        port += 3;
#endif
        var host = Environment.MachineName;
        var ip = System.Net.Dns.GetHostEntry(host).AddressList.First(x => x.AddressFamily is AddressFamily.InterNetwork);

        IP = new(ip, port);
        Dns = new(host, port, AddressFamily.InterNetwork);
        server = new TcpListener(ip, port);
        server.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    #if NET
                    var client = await server.AcceptTcpClientAsync(cts.Token);
                    #else
                    var client = await server.AcceptTcpClientAsync();
                    #endif
                    _ = Task.Run(() => RunClient(client, cts.Token));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        });
    }

    private static async Task? RunClient(TcpClient client, CancellationToken cancel)
    {
        // echo up to 4 bytes
        try
        {
            using var stream = client.GetStream();
            byte[] buffer = new byte[4];
            int bytesRead, count = 0;
            while (count < buffer.Length &&
                   (bytesRead = await stream.ReadAsync(buffer, count, buffer.Length - count, cancel)) > 0)
            {
                await stream.WriteAsync(buffer, count, bytesRead, cancel);
                count += bytesRead;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        finally
        {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        server.Stop();
        #if NET
        server.Dispose();
        #endif
    }
}
