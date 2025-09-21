using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Initialization();
            await CreateHostBuilder(args).Build().RunAsync();
        }

        private static void Initialization()
        {
            if (WindowsServiceHelpers.IsWindowsService() || SystemdHelpers.IsSystemdService()) Environment.CurrentDirectory = AppContext.BaseDirectory; //ȷ����������ڰ�װĿ¼
            var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH"); //Ϊ�˼��� docker ��ʽ����
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                Environment.CurrentDirectory = AppContext.BaseDirectory; //ȷ����������ڰ�װĿ¼
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseDiyServiceProvider()
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
                .UseWindowsService()
                .UseSystemd();

        private static string[] GetUrls(IConfiguration configuration)
        {
            var sections = configuration.GetSection("Server.Urls").GetChildren();

            if (!sections.Any()) throw new Exception("�޷���ȡ Server.Urls �����µ� ������Ϣ����鿴�����ļ���");
            List<string> usls = [];
            foreach (var section in sections)
            {
                if (string.IsNullOrEmpty(section.Value)) throw new Exception("Server.Urls �����µ� ������Ϣ�������⣬��鿴�����ļ���");
                usls.Add(section.Value);
            }

            return [.. usls];
        }

    }
}
