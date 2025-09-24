using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tool;
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
            await CreateHostBuilder(args).Build().RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseDiyServiceProvider()
                .ConfigureHostConfiguration((config) =>
                {
                    if (Exp.IsDocker)
                    {
                        string dockerConfigPath = Exp.IsDockerAppsettings();
                        config.AddJsonFile(dockerConfigPath, optional: true, reloadOnChange: true);
                        config.AddEnvironmentVariables();
                        config.AddCommandLine(args);
                    }
                })
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseKestrel((context, options) =>
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
                        .ConfigureHttpsDefaults(options =>
                        {
                            var obj = ObjectExtension.Provider.GetService(typeof(ServerCertificates));
                            if (obj is ServerCertificates serverCertificates)
                            {
                                serverCertificates.HttpsDefaults(options);
                            }
                        });

                        webBuilder.UseSetting(WebHostDefaults.ServerUrlsKey, GetUrlsString(context.Configuration));
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

        private static string[] GetUrls(IConfiguration configuration)
        {
            var sections = configuration.GetSection("Server.Urls").GetChildren();

            if (!sections.Any()) throw new Exception("无法获取 Server.Urls 集合下的 配置信息，请查看配置文件！");
            List<string> usls = [];
            foreach (var section in sections)
            {
                if (string.IsNullOrEmpty(section.Value)) throw new Exception("Server.Urls 集合下的 配置信息存在问题，请查看配置文件！");
                usls.Add(section.Value);
            }

            return [.. usls];
        }

        private static string GetUrlsString(IConfiguration configuration) => string.Join(';', GetUrls(configuration));
    }
}
