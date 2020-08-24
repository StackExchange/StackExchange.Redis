using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis.Server;

namespace KestrelRedisServer
{
    public class Startup : IDisposable
    {
        private readonly RespServer _server = new MemoryCacheRedisServer();

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
            => services.Add(new ServiceDescriptor(typeof(RespServer), _server));

        public void Dispose() => _server.Dispose();

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
        {
            _server.Shutdown.ContinueWith((t, s) =>
            {
                try
                {   // if the resp server is shutdown by a client: stop the kestrel server too
                    if (t.Result == RespServer.ShutdownReason.ClientInitiated)
                    {
                        ((IApplicationLifetime)s).StopApplication();
                    }
                }
                catch { /* Don't go boom on shutdown */ }
            }, lifetime);

            if (env.IsDevelopment()) app.UseDeveloperExceptionPage();
            app.Run(context => context.Response.WriteAsync(_server.GetStats()));
        }
    }
}
