using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading.Tasks;
using Tool;
using Tool.Sockets.NetFrame;
using WebProxy.Extensions;

namespace WebProxy.Service
{
    [method: DataNet(0)]
    public class ProxyApi() : DataBase
    {
        public string ConfigPath => Exp.IsDocker ? Exp.IsDockerAppsettings() : Path.Combine(Environment.CurrentDirectory, "appsettings.json");
            
        [DataNet(0, IsRelay = true)]
        public async ValueTask<IGoOut> Ping()
        {
            var okRequest = new { message = "OK" };
            return await JsonAsync(okRequest);
        }

        [DataNet(1, IsRelay = true)]
        public async ValueTask<IGoOut> Reload()
        {
            var configuration = ObjectExtension.Provider.GetService<DynamicConfiguration>();
            configuration?.TriggerReload();
            return await OkAsync();
        }

        [DataNet(2, IsRelay = true)]
        public async ValueTask<IGoOut> ReadConfig()
        {
            string content = null;
            string filePath = ConfigPath;
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                content = await File.ReadAllTextAsync(filePath);
            }
            return Json(content);
        }

        [DataNet(3, IsRelay = true)]
        public async ValueTask<IGoOut> WriteConfig(string newContent)
        {
            string filePath = ConfigPath;
            if (!string.IsNullOrEmpty(filePath))
            {
                await File.WriteAllTextAsync(filePath, newContent);
            }
            return Ok();
        }
    }
}
