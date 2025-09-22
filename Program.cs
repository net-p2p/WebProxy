using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
#if !DOCKER
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
#endif
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool.Utils;
using Tool.Web;
using WebProxy.Entiy;

namespace WebProxy
{
    public class Program
    {
        //sn -i "NixueProxy.pfx" VS_KEY_976755B58DBCE54E
        public static ServerCertificates SslCertificates { get; private set; }

        public static async Task Main(string[] args)
        {
            SslCertificates = new();
            await CreateHostBuilder(args).Build().RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseDiyServiceProvider()
                .ConfigureHostConfiguration((config) =>
                {
                    if (Extensions.Exp.IsDocker)
                    {
                        string dockerConfigPath = Path.Combine("config", "appsettings.json");
                        config.AddJsonFile(dockerConfigPath, optional: true, reloadOnChange: true);
                        config.AddEnvironmentVariables();
                        config.AddCommandLine(args);
                    }
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                    {
                        options.ConfigureHttpsDefaults(options =>
                        {
                            options.ServerCertificateSelector = (a, b) =>
                            {
                                if (string.IsNullOrEmpty(b)) b = "Default";
                                if (SslCertificates.GetSsl(b, out var certificate2))
                                {
                                    return certificate2;
                                }
                                return null;
                            };
                        });
                    });

                    webBuilder.ConfigureServices((a, b) =>
                    {
                        var serverUrls = GetUrls(a.Configuration);
                        webBuilder.UseUrls(serverUrls);
                    });
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.AddLogSave();
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

    }
}
