using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Tool.Utils;
using Tool.Web;
using WebProxy.DiyTransformFactory;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy
{
    public class ProxyConfigFilter : IProxyConfigFilter
    {
        public IConfiguration Configuration { get; }

        //private string AdminHost = string.Empty;
        //private bool W3CLogger = false;

        private readonly ILogger logger;

        public ProxyConfigFilter(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            Configuration = configuration;
            logger = loggerFactory.CreateLogger("Proxy");

            RegisterSsl();
            RegisterConfigChangeCallback();
        }

        public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
        {
            return ValueTask.FromResult(cluster);
        }

        public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig cluster, CancellationToken cancel)
        {
            if (cluster is null)
            {
                throw new Exception($"{nameof(cluster)} 对象不能为空！");
            }
            logger.LogInformation("{RouteId}.{ClusterId} [{Hosts}] --> {Now}", route.RouteId, route.ClusterId, string.Join(',', route.Match.Hosts), DateTime.Now.ToString("yy/MM/dd HH:mm:ss:fff"));
            return ValueTask.FromResult(route);
        }

        private void RegisterSsl()
        {
            logger.LogInformation("Ssl 证书载入...");
            Program.SslCertificates.Reset(Configuration, logger);//重新注册Ssl证书
            logger.LogInformation("Ssl 证书载入完成。");
        }

        // 设置配置变更监听
        void RegisterConfigChangeCallback()
        {
            var changeToken = Configuration.GetReloadToken();
            changeToken.RegisterChangeCallback(state =>
            {
                RegisterSsl(); // 重新注册以持续监听后续变更
                RegisterConfigChangeCallback();
            }, null);
        }
    }

    public class Startup(IConfiguration configuration)
    {
        public IConfiguration Configuration { get; } = configuration;

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            SetFormOptions(services);// 配置输入大小

            services.AddRequestTimeouts(options =>
            {
                options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy() { Timeout = TimeSpan.FromSeconds(100) };
                for (int i = 1; i <= 60; i++)
                {
                    options.AddPolicy($"{i}s", TimeSpan.FromSeconds(i));
                    options.AddPolicy($"{i}m", TimeSpan.FromMinutes(i));
                }
            });

            services.AddReverseProxy().LoadFromConfig(Configuration.GetSection("ReverseProxy"))
                //.AddTransformFactory<W3CLoggerTransformFactory>()
                .AddTransformFactory<DiyTypeTransformFactory>()
                .AddConfigFilter<ProxyConfigFilter>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler(AllException);
            }

            app.UseRouting();
            app.UseRequestTimeouts();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapReverseProxy(proxyPipeline => 
                {
                   //注册其他中间件
                });
            });
        }

        public async Task AllException(HttpContext context, Exception exception)
        {
            await context.Response.WriteAsync("An unknown error has occurred!");
            Log.Error("捕获全局异常：", exception);//"Log/Risk/"
        }

        private static void SetFormOptions(IServiceCollection services) //取消 ASP.NET Core 控制器的限制，正常情况以下配置是无效的
        {
            services.SetFormOptions(config =>
            {
                // 取消所有限制，设置成最大值
                config.BufferBody = true;
                config.MemoryBufferThreshold = int.MaxValue;
                config.BufferBodyLengthLimit = long.MaxValue;
                config.ValueCountLimit = int.MaxValue;
                config.KeyLengthLimit = int.MaxValue;
                config.ValueLengthLimit = int.MaxValue;
                config.MultipartBoundaryLengthLimit = int.MaxValue;
                config.MultipartHeadersCountLimit = int.MaxValue;
                config.MultipartHeadersLengthLimit = int.MaxValue;
                config.MultipartBodyLengthLimit = long.MaxValue;
            });
        }
    }
}
