using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using WebProxy.Extensions;

namespace WebProxy.Entiy
{
    public class ServerCertificates
    {
        private readonly bool IsDocker;

        public IReadOnlyDictionary<string, Certificates> Certificates { get; private set; }

        public ServerCertificates()
        {
            IsDocker = Extensions.Exp.IsDocker; //为了兼容 docker 方式运行
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
                if (string.IsNullOrWhiteSpace(domain)) throw new Exception("HttpSsl 集合下的配置信息，Domain 存在空值，请查看配置文件！");

                if (section.IsSslPath("Pem", out string[] sslpaths) || section.IsSslPath("Pfx", out sslpaths))
                {
                    string sslPath = GetPlatformPath(sslpaths[1]), ssltype = sslpaths[0];
                    if (File.Exists(sslPath))
                    {
                        Certificates certificate = new(domain, sslpaths[0], sslPath, sslpaths[2]);
                        if (certificate.IsError)
                        {
                            logger.LogError("加载证书[{ssltype}]：{Domain} 失败，错误：{Error}", ssltype, domain, certificate.Error);
                        }
                        else
                        {
                            logger.LogInformation("加载证书[{ssltype}]：{Domain}", ssltype, domain);
                        }
                        if(!pairs.TryAdd(domain, certificate)) 
                        {
                            certificate.Dispose();
                        }
                    }
                    else
                    {
                        logger.LogError("加载证书[{ssltype}]：{Domain} 失败，路径不存在：{sslPath}", ssltype, domain, sslPath);
                    }
                }
                else
                {
                    logger.LogError("加载证书：{Domain} 失败，不存在：Pem or Pfx 证书路径配置！", domain);
                }
            }

            Certificates = pairs.AsReadOnly();
        }

        private string GetPlatformPath(string path) 
        {
            if (IsDocker)
            {
                string dockerConfigPath = Path.Combine("/app/certs", path);
                return dockerConfigPath;
            }
            return path;
        }
    }
}
