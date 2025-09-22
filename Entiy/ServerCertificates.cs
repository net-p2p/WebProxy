using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace WebProxy.Entiy
{
    public class ServerCertificates
    {
        private readonly string platform;

        public IReadOnlyDictionary<string, Certificates> Certificates { get; private set; }

        public ServerCertificates()
        {
            platform = Environment.GetEnvironmentVariable("Platform"); //为了兼容 docker 方式运行
            Certificates = new Dictionary<string, Certificates>(StringComparer.OrdinalIgnoreCase);
        }

        public ServerCertificates(IConfiguration configuration, ILogger logger)
        {
            GetServerCertificate(configuration, logger);
        }

        public void Reset(IConfiguration configuration, ILogger logger)
        {
            GetServerCertificate(configuration, logger);
        }

        public bool GetSsl(string domain, out X509Certificate2 certificate) 
        {
            var _contains = Certificates.TryGetValue(domain, out var _certificate);
            if (_contains)
            {
                certificate = _certificate.Certificate;
            }
            else
            {
                certificate = null;
            }
            return _contains;
        }

        private void GetServerCertificate(IConfiguration configuration, ILogger logger)
        {
            var sections = configuration.GetSection("HttpSsl").GetChildren();

            if (!sections.Any()) { Certificates = new Dictionary<string, Certificates>(StringComparer.OrdinalIgnoreCase); return; } // throw new Exception("无法获取 HttpSsl 集合下的 配置信息，请查看配置文件！");

            Dictionary<string, Certificates> pairs = new(StringComparer.OrdinalIgnoreCase);

            foreach (var section in sections)
            {
                var domain = section.GetValue<string>("Domain");
                if (string.IsNullOrWhiteSpace(domain)) throw new Exception("HttpSsl 集合下的 配置信息,Domain 存在空值！，请查看配置文件！");
                string sslPath = GetPlatformPath(section.GetValue<string>("SslPath"));
                if (File.Exists(sslPath))
                {
                    Certificates certificate = new(domain, GetPlatformPath(section.GetValue<string>("SslPath")), section.GetValue<string>("Password"));
                    logger.LogInformation("加载证书：{Domain}", domain);
                    pairs.TryAdd(domain, certificate);
                }
                else
                {
                    logger.LogError("加载证书：{Domain} 失败，路径不存在：{sslPath}", domain, sslPath);
                }
            }

            Certificates = pairs.AsReadOnly();
        }

        private string GetPlatformPath(string path) 
        {
            if (string.Equals(platform, "Docker", StringComparison.OrdinalIgnoreCase))
            {
                string dockerConfigPath = Path.Combine("certs", path);
                return dockerConfigPath;
            }
            return path;
        }
    }
}
