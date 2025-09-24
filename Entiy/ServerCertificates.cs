using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using WebProxy.Extensions;

namespace WebProxy.Entiy
{
    public class ServerCertificates
    {
        private readonly bool IsDocker;
        private readonly ILogger logger;
        private readonly IConfiguration configuration;

        public ConcurrentDictionary<string, Certificates> Certificates { get; }

        private ServerCertificates()
        {
            IsDocker = Exp.IsDocker; //为了兼容 docker 方式运行
            Certificates = new(StringComparer.OrdinalIgnoreCase);
        }

        public ServerCertificates(IConfiguration configuration, ILoggerFactory loggerFactory) : this()
        {
            logger = loggerFactory.CreateLogger("SSL");
            this.configuration = configuration;
            GetServerCertificate();
        }

        public void Reset()
        {
            GetServerCertificate();
        }

        public bool GetSsl(Microsoft.AspNetCore.Connections.ConnectionContext context, string domain, ref X509Certificate2 certificate)
        {
            if (string.IsNullOrEmpty(domain)) domain = "Default";
            var _contains = Certificates.TryGetValue(domain, out var _certificate);
            if (_contains)
            {
                if (_certificate.IsDelete)
                {
                    logger.LogError("连接[{EndPoint}]：{Domain} 握手失败，因证书已删除！", context.RemoteEndPoint, domain);
                    return false;
                }
                if (_certificate.IsDispose)
                {
                    logger.LogError("连接[{EndPoint}]：{Domain} 握手失败，因证书已释放！", context.RemoteEndPoint, domain);
                    return false;
                }
                certificate = _certificate.Certificate;
                return true;
            }
            logger.LogError("连接[{EndPoint}]：{Domain} 握手失败，因无可用证书！", context.RemoteEndPoint, domain);
            return false;
        }

        public X509Certificate2 OnServerCertificate(Microsoft.AspNetCore.Connections.ConnectionContext context, string hostName) 
        {
            X509Certificate2 certificate2 = null;
            if (GetSsl(context, hostName, ref certificate2))
            {
                return certificate2;
            }
            return null;
        }

        public void HttpsDefaults(HttpsConnectionAdapterOptions options)
        {
            //options.SslProtocols = SslProtocols.Tls12;
            options.ServerCertificateSelector = OnServerCertificate;
            //options.OnAuthenticate = (context, ssl) =>
            //{
                
            //};

            //options.ClientCertificateValidation = (a, b, c) =>
            //{
            //    return true;
            //};
        }

        private void GetServerCertificate()
        {
            var sections = configuration.GetSection("HttpSsl").GetChildren();
            foreach (var certificate in Certificates) certificate.Value.Delete();
            foreach (var section in sections)
            {
                var domain = section.GetValue<string>("Domain");
                if (string.IsNullOrWhiteSpace(domain)) throw new Exception("HttpSsl 集合下的配置信息，Domain 存在空值，请查看配置文件！");

                if (section.IsSslPath("Pem", out string[] sslpaths) || section.IsSslPath("Pfx", out sslpaths))
                {
                    string sslPath1 = GetPlatformPath(sslpaths[1]), ssltype = sslpaths[0], sslPath2 = GetPlatformPemPath(ssltype, sslpaths[2]);
                    if (File.Exists(sslPath1))
                    {
                        Certificates certificate = new(domain, ssltype, sslPath1, sslPath2);
                        if (certificate.IsError)
                        {
                            logger.LogError("加载证书[{ssltype}]：{Domain} 失败，错误：{Error}", ssltype, domain, certificate.Error);
                            continue;
                        }
                        else
                        {
                            logger.LogInformation("加载证书[{ssltype}]：{Domain}", ssltype, domain);
                        }
                        Certificates.AddOrUpdate(domain, certificate, (key, oldcert) =>
                        {
                            oldcert.Dispose();
                            return certificate;
                        });
                    }
                    else
                    {
                        logger.LogError("加载证书[{ssltype}]：{Domain} 失败，路径不存在：{sslPath}", ssltype, domain, sslPath1);
                    }
                }
                else
                {
                    logger.LogError("加载证书：{Domain} 失败，不存在：Pem or Pfx 证书路径配置！", domain);
                }
            }
        }

        private string GetPlatformPath(string path)
        {
            if (IsDocker)
            {
                string dockerConfigPath = Path.Combine(Environment.CurrentDirectory, "certs", path);
                return dockerConfigPath;
            }
            return path;
        }

        private string GetPlatformPemPath(string ssltype, string path)
        {
            if (ssltype == "Pem")
            {
                return GetPlatformPath(path);
            }
            return path;
        }
    }
}
