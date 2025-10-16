using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;
using Tool.Web;
using WebProxy.Entiy;
using WebProxy.Extensions;

namespace WebProxy
{
    public class Program
    {
        //sn -i "NixueProxy.pfx" VS_KEY_976755B58DBCE54E

        public const int _1MB = 1024 * 1024;

        public static async Task Main(string[] args)
        {
            //AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            //{
            //    Console.WriteLine($"[Unhandled] {e.ExceptionObject}");
            //};
            //TaskScheduler.UnobservedTaskException += (s, e) =>
            //{
            //    Console.WriteLine($"[Unobserved] {e.Exception}");
            //};
            await CreateHostBuilder(args).Build().RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseDiyServiceProvider()
                .ConfigureHostConfiguration((config) =>
                {
                    string configPath = Exp.IsDocker ? Exp.IsDockerAppsettings() : Exp.IsAppsettings();
                    config.AddJsonFile(configPath, optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args);
                })
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseSockets(options =>
                    {
                        options.Backlog = 5120;                                 // 最大请求队列排队数
                    })
                    .UseKestrel((context, options) =>
                    {
                        options.LimitsOptions(options =>
                        {
                            options.MaxRequestBodySize = 100L * _1MB;           // 100MB
                            options.MaxRequestBufferSize = null;                // 不限制缓冲
                            options.MaxRequestHeaderCount = 10000;              // 最大请求头数量
                            options.MaxRequestHeadersTotalSize = 10 * _1MB;     // 最大请求头字节数
                            options.MaxRequestLineSize = 10 * _1MB;             // 最大请求地址长度
                            options.MaxResponseBufferSize = null;               // 不限制响应缓冲

                            //options.Http2.MaxStreamsPerConnection = 100;
                        })
                        .ConfigureEndpointDefaults(options =>
                        {
                            var isHttps = Exp.IsEndpointHttps(options.EndPoint);
                            if (isHttps)
                            {
                                options.UseConnectionLogging($"TlsConnection.{options.EndPoint}");
                                var obj = options.ApplicationServices.GetService(typeof(ServerCertificates));
                                if (obj is ServerCertificates serverCertificates)
                                {
                                    var tlsHandshake = new TlsHandshakeCallbackOptions
                                    {
                                        OnConnectionState = options,
                                        OnConnection = serverCertificates.HttpsConnectionAsync,
                                    };

                                    options.UseHttps(tlsHandshake);
                                }
                                else
                                {
                                    throw new Exception("无法完成动态证书绑定！");
                                }
                            }
                            else
                            {
                                options.UseConnectionLogging($"HttpConnection.{options.EndPoint}");
                            }
                        });

                        //options.ConfigureHttpsDefaults(options =>
                        //{
                        //    var obj = ObjectExtension.Provider.GetService(typeof(ServerCertificates));
                        //    if (obj is ServerCertificates serverCertificates)
                        //    {
                        //        serverCertificates.HttpsDefaults(options);
                        //    }
                        //});

                        webBuilder.UseSetting(WebHostDefaults.ServerUrlsKey, context.Configuration.GetUrlsString());
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.AddLogSave();
                    })
                    .UseStartup<Startup>();
                })
#if !DOCKER
                .UseWindowsService(conf => Environment.CurrentDirectory = AppContext.BaseDirectory) //确保程序访问在安装目录
                .UseSystemd()
#endif
            ;
    }
}
