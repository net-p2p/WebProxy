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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using WebProxy.Extensions;

namespace WebProxy.Entiy
{
    public class ServerCertificates
    {
        private readonly SslServerAuthenticationOptions defaultSslServer;
        private readonly bool IsDocker;
        private readonly ILogger logger;
        private readonly IConfiguration configuration;

        public ConcurrentDictionary<string, Certificates> Certificates { get; }

        private ServerCertificates()
        {
            Exp.IsCertsPath();
            IsDocker = Exp.IsDocker; //为了兼容 docker 方式运行
            Certificates = new(StringComparer.OrdinalIgnoreCase);
            defaultSslServer = new() { ServerCertificateSelectionCallback = ServerCertificateSelection };
        }

        public ServerCertificates(IConfiguration configuration, ILoggerFactory loggerFactory) : this()
        {
            logger = loggerFactory.CreateLogger("SSL");
            logger.LogInformation("已为[{OSDescription}]平台 初始化 TLS", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            this.configuration = configuration;
            GetServerCertificate();
        }

        private bool GetDefaultCert(out Certificates _certificate)
        {
            const string domain = "Default";
            return Certificates.TryGetValue(domain, out _certificate);
        }

        public void Reset()
        {
            GetServerCertificate();
        }

        public bool GetSsl(ConnectionContext context, string domain, out Certificates certificate)
        {
            if (!string.IsNullOrEmpty(domain) && Certificates.TryGetValue(domain, out var _certificate))
            {
                certificate = _certificate;
                return true;
            }
            if (GetDefaultCert(out _certificate))
            {
                certificate = _certificate;
                return true;
            }
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("连接[{Remote}]:[{Local}]=[{Domain}] 无域名适配也无默认证书兜底！", context.RemoteEndPoint, context.LocalEndPoint, domain);
            }
            certificate = null;
            return false;
        }

        public bool TryServerCertificate(ConnectionContext context, string hostName, out SslStreamCertificateContext sslStreamCertificateContext, out CertEntiy certEntiy)
        {
            if (GetSsl(context, hostName, out Certificates certificate))
            {
                logger.LogDebug("连接[{EndPoint}]：{Domain} 已连接！", context.RemoteEndPoint, hostName);

                context.ConnectionClosed.Register(() =>
                {
                    logger.LogDebug("连接[{EndPoint}]：{Domain} 已断开！", context.RemoteEndPoint, hostName);
                    certificate.ReturnCert();
                });
                certEntiy = certificate.CertEntiy;
                sslStreamCertificateContext = certificate.BorrowCert();
                return true;
            }
            certEntiy = null;
            sslStreamCertificateContext = null;
            return false;
        }

        public async ValueTask<SslServerAuthenticationOptions> HttpsConnectionAsync(TlsHandshakeCallbackContext context)
        {
            try
            {
                if (TryServerCertificate(context.Connection, context.ClientHelloInfo.ServerName, out var sslStreamCertificateContext, out var certEntiy))
                {
                    var authenticationOptions = new SslServerAuthenticationOptions
                    {
                        ApplicationProtocols = certEntiy.ApplicationProtocols,
                        ServerCertificateContext = sslStreamCertificateContext,
                        EnabledSslProtocols = certEntiy.EnabledSslProtocols,
                        AllowRenegotiation = certEntiy.AllowRenegotiation,
                        AllowTlsResume = certEntiy.AllowTlsResume,
                        ClientCertificateRequired = certEntiy.ClientCertificateRequired,
                        CertificateRevocationCheckMode = certEntiy.CertificateRevocationCheckMode,
                        EncryptionPolicy = certEntiy.EncryptionPolicy,
                    };

                    await ServerTlsCiphers(authenticationOptions, certEntiy.TlsCiphers);
                    return authenticationOptions;
                }

                return AbortSslServer(context);
            }
            catch (Exception ex)
            {
                logger.LogError("TlsHandshake：{ex}", ex);
                return AbortSslServer(context);
            }
        }

        private static ValueTask ServerTlsCiphers(SslServerAuthenticationOptions authenticationOptions, List<TlsCipherSuite> TlsCiphers)
        {
            if (!(OperatingSystem.IsWindows() || OperatingSystem.IsAndroid()))
            {
                if (TlsCiphers.Count is not 0)
                {
                    authenticationOptions.CipherSuitesPolicy = new CipherSuitesPolicy(TlsCiphers);
                }
                //else
                //{
                //    var cipherMode = Environment.GetEnvironmentVariable("TLS_CIPHER_MODE") ?? "Secure";
                //    if (cipherMode.Equals("Secure", StringComparison.OrdinalIgnoreCase))
                //    {
                //        authenticationOptions.CipherSuitesPolicy = new CipherSuitesPolicy(
                //        [
                //            // TLS 1.3 Suites (OpenSSL ignores but ok to list)
                //            TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                //            TlsCipherSuite.TLS_AES_128_GCM_SHA256,
                //            TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,

                //            //TlsCipherSuite.TLS_ECCPWD_WITH_AES_256_GCM_SHA384, //实验阶段

                //            // TLS 1.2 Suites
                //            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                //            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                //            TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,

                //            // TLS 1.2 补充套件
                //            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256,
                //            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384,
                //            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
                //            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,

                //            //// TLS 1.1 相关套件
                //            //TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
                //            //TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
                //            //TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA256,
                //            //TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256,

                //            //// TLS 1.0 相关套件（不推荐）
                //            //TlsCipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA,
                //            //TlsCipherSuite.TLS_ECDHE_RSA_WITH_3DES_EDE_CBC_SHA
                //        ]);

                //        logger.LogDebug("TLS_CIPHER_MODE=Secure - 使用主流套件策略与Nginx一致");
                //    }
                //    else if (cipherMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                //    {
                //        logger.LogDebug("TLS_CIPHER_MODE=Auto - 使用系统默认套件策略");
                //    }
                //}
            }

            return ValueTask.CompletedTask;
        }

        private SslServerAuthenticationOptions AbortSslServer(TlsHandshakeCallbackContext context)
        {
            context.Connection.Abort();
            logger.LogDebug("连接[{Remote}]：{Local} 已断开！", context.Connection.RemoteEndPoint, context.Connection.LocalEndPoint);
            return defaultSslServer;
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
                if (domain.Equals("AliasHosts")) continue;
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
                        _certEntiys.Add(new CertEntiy
                        {
                            Domain = domain,
                            SslType = ssltype,
                            Arg0 = arg0,
                            Arg1 = arg1,
                            ApplicationProtocols = section.GetApplicationProtocols(),
                            EnabledSslProtocols = section.GetSslProtocols(),
                            TlsCiphers = section.GetTlsCiphers(),
                            AllowRenegotiation = section.GetConfBool("AllowRenegotiation", false),
                            AllowTlsResume = section.GetConfBool("AllowTlsResume", true),
                            ClientCertificateRequired = section.GetConfBool("ClientCertificateRequired", false),
                            CertificateRevocationCheckMode = section.GetConfEnum("CertificateRevocationCheckMode", X509RevocationMode.NoCheck),
                            EncryptionPolicy = section.GetConfEnum("EncryptionPolicy", EncryptionPolicy.RequireEncryption),
                        });
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
                    if (pair.Key.Equals(certificate.Domain, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation("移除证书[{ssltype}]：{Domain}", certificate.SslType, certificate.Domain);
                        certificate.Delete();
                    }
                    Certificates.TryRemove(pair);
                }
                else if (!pair.Key.Equals(pair.Value.Domain, StringComparison.OrdinalIgnoreCase))
                {
                    Certificates.TryRemove(pair);
                }
            }
        }

        private Dictionary<string, string[]> TryCertHosts()
        {
            var sections = configuration.GetSection("HttpSsl:AliasHosts").GetChildren();
            Dictionary<string, string[]> pairs = new(StringComparer.OrdinalIgnoreCase);
            foreach (var section in sections)
            {
                var domain = section.Key;
                if (string.IsNullOrEmpty(domain))
                {
                    logger.LogError("域名别名 [AliasHosts]：不能为空。");
                    continue;
                }
                HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
                foreach (var item in section.GetChildren())
                {
                    string host = item.Value;
                    if (!string.IsNullOrEmpty(host) && !domain.Equals(host, StringComparison.OrdinalIgnoreCase))
                    {
                        hosts.Add(host);
                    }
                }
                pairs.TryAdd(section.Key, [.. hosts]);
            }
            return pairs;
        }

        private void GetServerCertificate()
        {
            //var certificate2 = CertificateLoader.LoadFromStoreCert(sslStreamCertificate.TargetCertificate.Subject, sslStreamCertificate.TargetCertificate.Issuer, StoreLocation.LocalMachine, true);
            if (TryCertEntiys(out List<CertEntiy> certEntiys))
            {
                DeleteCert(certEntiys);

                foreach (var certEntiy in certEntiys)
                {
                    Certificates certificate = new(certEntiy, logger);
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
                            oldcert.UpCertEntiy(certEntiy, logger);
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

            var certHosts = TryCertHosts(); //别名域名绑定证书（关联域名头）
            foreach (var pair in Certificates.ToArray())
            {
                if (certHosts.TryGetValue(pair.Key, out string[] hosts))
                {
                    foreach (var host in hosts)
                    {
                        Certificates.TryAdd(host, pair.Value);
                    }
                }
            }
        }

        private string GetPlatformPath(string path)
        {
            if (IsDocker)
            {
                string dockerPath = Path.Combine(Environment.CurrentDirectory, "certs", path);
                return dockerPath;
            }
            return path;
        }

        private static X509Certificate2 ServerCertificateSelection(object sender, string hostName)
        {
            return null; //无匹配的 hostName 返回空，用于断开与客户端的连接。
            //try
            //{
            //    var localhostCert = CertificateLoader.LoadFromStoreCert("localhost", "My", StoreLocation.CurrentUser, allowInvalid: true); //默认本地证书
            //    return localhostCert;
            //}
            //catch (Exception)
            //{
            //    return null;
            //}
        }
    }
}
