using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;

namespace KestrelRedisServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseKestrel(options =>
                {
                    // TCP 6379
                    options.ListenLocalhost(8007, builder =>
                    {
                        builder.UseConnectionHandler<RedisConnectionHandler>();
                    });
                });
    }
}
