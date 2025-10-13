using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Threading.Tasks;
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
            logger.LogInformation("已为[{OSDescription}]平台 初始化 TLS", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            this.configuration = configuration;
            GetServerCertificate();
        }

        public void Reset()
        {
            GetServerCertificate();
        }

        public bool GetSsl(ConnectionContext context, string domain, out Certificates certificate)
        {
            if (string.IsNullOrEmpty(domain)) domain = "Default";
            var _contains = Certificates.TryGetValue(domain, out var _certificate);
            if (_contains)
            {
                certificate = _certificate;
                return true;
            }
            if (domain is not "Default") logger.LogError("连接[{EndPoint}]：{Domain} 握手失败，因无可用证书！", context.RemoteEndPoint, domain);
            certificate = null;
            return false;
        }

        public SslStreamCertificateContext OnServerCertificate(ConnectionContext context, string hostName)
        {
            if (GetSsl(context, hostName, out Certificates certificate))
            {
                logger.LogDebug("连接[{EndPoint}]：{Domain} 已连接！", context.RemoteEndPoint, hostName);

                context.ConnectionClosed.Register(() =>
                {
                    logger.LogDebug("连接[{EndPoint}]：{Domain} 已断开！", context.RemoteEndPoint, hostName);
                    certificate.ReturnCert();
                });

                return certificate.BorrowCert();
            }
            return null;
        }

        public async ValueTask<SslServerAuthenticationOptions> HttpsConnectionAsync(TlsHandshakeCallbackContext context)
        {
            try
            {
                var sslStreamCertificate = OnServerCertificate(context.Connection, context.ClientHelloInfo.ServerName) ?? throw new InvalidOperationException("证书加载失败");

                var authenticationOptions = new SslServerAuthenticationOptions
                {
                    //ServerCertificate = localhostCert,
                    ServerCertificateContext = sslStreamCertificate,
                    //EnabledSslProtocols = SslProtocols.Tls12,
                    //ClientCertificateRequired = false, //true,
                };

                if (!(OperatingSystem.IsWindows() || OperatingSystem.IsAndroid()))
                {
                    //ssl.EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
                    var cipherMode = Environment.GetEnvironmentVariable("TLS_CIPHER_MODE") ?? "Secure";
                    if (cipherMode.Equals("Secure", StringComparison.OrdinalIgnoreCase))
                    {
                        authenticationOptions.CipherSuitesPolicy = new CipherSuitesPolicy(
                        [
                            // TLS 1.3 Suites (OpenSSL ignores but ok to list)
                            TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                            TlsCipherSuite.TLS_AES_128_GCM_SHA256,
                            TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,

                            //TlsCipherSuite.TLS_ECCPWD_WITH_AES_256_GCM_SHA384, //实验阶段

                            // TLS 1.2 Suites
                            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                            TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256
                        ]);

                        logger.LogDebug("TLS_CIPHER_MODE=Secure - 使用主流套件策略与Nginx一致");
                    }
                    else if (cipherMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogDebug("TLS_CIPHER_MODE=Auto - 使用系统默认套件策略");
                    }
                }

                await Task.CompletedTask;
                return authenticationOptions;
            }
            catch (Exception ex)
            {
                logger.LogError("TlsHandshake：{ex}", ex);
                return null;
            }
        }

        //public void HttpsDefaults(HttpsConnectionAdapterOptions options)
        //{
        //    //options.ServerCertificateSelector = OnServerCertificate;
        //    options.OnAuthenticate = (context, ssl) =>
        //    {

        //    };

        //    //options.ClientCertificateValidation = (a, b, c) =>
        //    //{
        //    //    return true;
        //    //};
        //}

        private bool TryCertEntiys(out List<CertEntiy> certEntiys)
        {
            var sections = configuration.GetSection("HttpSsl").GetChildren();

            List<CertEntiy> _certEntiys = [];
            foreach (var section in sections)
            {
                var domain = section.Key;
                if (string.IsNullOrWhiteSpace(domain))
                {
                    logger.LogError("HttpSsl 集合下的配置信息，Domain 存在空值，请查看配置文件！");
                    certEntiys = null;
                    return false;
                }

                if (section.TrySslPath(out string[] sslpaths))
                {
                    string ssltype = sslpaths[0], arg0 = GetPlatformPath(sslpaths[1]), arg1 = sslpaths[2];
                    if (File.Exists(arg0))
                    {
                        if (ssltype.Equals("Pem"))
                        {
                            arg1 = GetPlatformPath(arg1);
                            if (!File.Exists(arg1))
                            {
                                logger.LogError("加载证书[{ssltype}]：{Domain} 失败，密钥路径不存在：{arg1}", ssltype, domain, arg1);
                                certEntiys = null;
                                return false;
                            }
                        }
                        _certEntiys.Add(new CertEntiy { Domain = domain, SslType = ssltype, Arg0 = arg0, Arg1 = arg1 });
                    }
                    else
                    {
                        logger.LogError("加载证书[{ssltype}]：{Domain} 失败，路径不存在：{arg0}", ssltype, domain, arg0);
                        certEntiys = null;
                        return false;
                    }
                }
                else
                {
                    logger.LogError("加载证书：{Domain} 失败，不存在：Pem or Pfx 证书路径配置！", domain);
                    certEntiys = null;
                    return false;
                }
            }

            certEntiys = _certEntiys;
            return true;
        }

        private void DeleteCert(List<CertEntiy> certEntiys)
        {
            foreach (var pair in Certificates)
            {
                if (!certEntiys.Any(entiy => entiy.Domain.Equals(pair.Key)))
                {
                    var certificate = pair.Value;
                    logger.LogInformation("移除证书[{ssltype}]：{Domain}", certificate.SslType, certificate.Domain);
                    certificate.Delete();
                    Certificates.TryRemove(pair);
                }
            }
        }

        private void GetServerCertificate()
        {
            //var localhostCert = CertificateLoader.LoadFromStoreCert("localhost", "My", StoreLocation.CurrentUser, allowInvalid: true); //默认本地证书
            //var certificate2 = CertificateLoader.LoadFromStoreCert(sslStreamCertificate.TargetCertificate.Subject, sslStreamCertificate.TargetCertificate.Issuer, StoreLocation.LocalMachine, true);
            if (TryCertEntiys(out List<CertEntiy> certEntiys))
            {
                DeleteCert(certEntiys);

                foreach (var certEntiy in certEntiys)
                {
                    Certificates certificate = new(certEntiy);
                    if (certificate.IsError)
                    {
                        logger.LogError("加载证书[{ssltype}]：{Domain} 失败，错误：{Error}", certEntiy.SslType, certEntiy.Domain, certificate.Error);
                        continue;
                    }
                    Certificates.AddOrUpdate(certEntiy.Domain,
                    key =>
                    {
                        logger.LogInformation("载入证书[{ssltype}]：{Domain}", certEntiy.SslType, certEntiy.Domain);
                        certificate.GetSubjects(logger);
                        return certificate;
                    },
                    (key, oldcert) =>
                    {
                        if (oldcert.CertHash.Equals(certificate.CertHash))
                        {
                            logger.LogInformation("无需更新[{ssltype}]：{Domain}", certEntiy.SslType, certEntiy.Domain);
                            certificate.Dispose();
                            return oldcert;
                        }
                        else
                        {
                            logger.LogInformation("更新证书[{ssltype}]：{Domain}", certEntiy.SslType, certEntiy.Domain);
                            oldcert.Delete();
                            certificate.GetSubjects(logger);
                            return certificate;
                        }
                    });
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
    }
}
