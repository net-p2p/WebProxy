using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Tool.Utils.Data;

namespace WebProxy.Entiy
{
    public class ServerCertificates
    {
        public IReadOnlyDictionary<string, Certificates> Certificates { get; private set; }

        public ServerCertificates()
        {
            Certificates = new Dictionary<string, Certificates>();
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

            if (!sections.Any()) { Certificates = new Dictionary<string, Certificates>(); return; } // throw new Exception("无法获取 HttpSsl 集合下的 配置信息，请查看配置文件！");

            Dictionary<string, Certificates> pairs = new();

            foreach (var section in sections)
            {
                var domain = section.GetValue<string>("Domain");
                if (string.IsNullOrWhiteSpace(domain)) throw new Exception("HttpSsl 集合下的 配置信息,Domain 存在空值！，请查看配置文件！");
                Certificates certificate = new(domain, section.GetValue<string>("SslPath"), section.GetValue<string>("Password"));
                logger.LogInformation("加载证书：{Domain}", domain);
                pairs.TryAdd(domain, certificate);
            }

            Certificates = pairs.AsReadOnly();
        }
    }
}
