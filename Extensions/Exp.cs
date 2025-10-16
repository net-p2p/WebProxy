using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.Extensions
{
    public static class Exp
    {
        #region 判断套接字绑定数据

        private static int urlIndex;
        private static List<string> urls = [];

        private static string[] GetUrls(this IConfiguration configuration)
        {
            var sections = configuration.GetSection("Server.Urls").GetChildren();
            if (!sections.Any()) throw new Exception("无法获取 Server.Urls 集合下的 配置信息，请查看配置文件！");
            urls.Clear();
            foreach (var section in sections)
            {
                if (string.IsNullOrEmpty(section.Value)) throw new Exception("Server.Urls 集合下的 配置信息存在问题，请查看配置文件！");
                urls.Add(section.Value);
            }

            return [.. urls];
        }

        public static string GetUrlsString(this IConfiguration configuration) => string.Join(';', GetUrls(configuration));

        public static bool IsEndpointHttps(EndPoint endpoint)
        {
            try
            {
                if (urls.Count > urlIndex)
                {
                    string url = urls[urlIndex];
                    var endPoint = ParseAddress(url, out bool https);
                    if (endPoint.Equals(endpoint))
                    {
                        return https;
                    }
                }
            }
            finally
            {
                urlIndex++;
            }
            return false;
        }

        private static EndPoint ParseAddress(string address, out bool https)
        {
            var parsedAddress = BindingAddress.Parse(address);
            https = parsedAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

            if (parsedAddress.IsUnixPipe)
            {
                return new UnixDomainSocketEndPoint(parsedAddress.UnixPipePath);
            }
            else if (parsedAddress.IsNamedPipe)
            {
                return new NamedPipeEndPoint(parsedAddress.NamedPipeName);
            }
            else if (IsLocalhost(parsedAddress.Host, out var prefix))
            {
                return prefix is null ? new IPEndPoint(IPAddress.Loopback, parsedAddress.Port) : new IPEndPoint(IPAddress.IPv6Any, parsedAddress.Port);
            }
            else if (TryCreateIPEndPoint(parsedAddress, out var endpoint))
            {
                return endpoint;
            }
            else
            {
                return new IPEndPoint(IPAddress.IPv6Any, parsedAddress.Port);
            }

            static bool IsLocalhost(string host, out string prefix)
            {
                if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    prefix = null;
                    return true;
                }

                const string localhostTld = ".localhost";
                if (host.Length > localhostTld.Length && host.EndsWith(localhostTld, StringComparison.OrdinalIgnoreCase))
                {
                    prefix = host[..^localhostTld.Length];
                    return true;
                }

                prefix = null;
                return false;
            }

            static bool TryCreateIPEndPoint(BindingAddress address, [NotNullWhen(true)] out IPEndPoint endpoint)
            {
                if (!IPAddress.TryParse(address.Host, out var ip))
                {
                    endpoint = null;
                    return false;
                }

                endpoint = new IPEndPoint(ip, address.Port);
                return true;
            }
        }

        #endregion

        /// <summary>
        /// 将字符串编码为最多 6 字节，格式化为 IP:端口 格式。
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <param name="encoding">编码方式，默认 UTF-8</param>
        /// <returns>格式为 x.x.x.x:port 的字符串</returns>
        /// <exception cref="ArgumentException">编码后超过 6 字节时抛出</exception>
        public static string EncodeAsIpPort(string input, Encoding encoding)
        {
            encoding ??= Encoding.UTF8;

            byte[] bytes = encoding.GetBytes(input ?? "");

            if (bytes.Length > 6)
                throw new ArgumentException("输入字符串编码后长度不能超过 6 字节");

            byte[] result = new byte[6];
            Array.Copy(bytes, result, bytes.Length); // 不足的自动是 0

            // 构造 IP:Port 格式
            int port = (result[4] << 8) + result[5];
            return $"{result[0]}.{result[1]}.{result[2]}.{result[3]}:{port}";
        }

        public static double ElapsedMilliseconds(this Stopwatch stopwatch) => ElapsedMicroseconds(stopwatch) / 1_000.0;

        public static double ElapsedMicroseconds(this Stopwatch stopwatch) => ElapsedNanoseconds(stopwatch) / 1_000.0;// 计算微秒

        public static long ElapsedNanoseconds(this Stopwatch stopwatch) => stopwatch.ElapsedTicks * 1_000_000_000L / Stopwatch.Frequency; // 计算纳秒

        public static StringBuilder LogLine(this StringBuilder builder)
        {
            return builder.AppendLine().Append(' ').Append(' ');
        }

        public static StringBuilder LogAppend(this StringBuilder builder, object log)
        {
            return builder.Append(log).Append(' ');
        }

        public static KestrelServerOptions LimitsOptions(this KestrelServerOptions options, Action<KestrelServerLimits> action)
        {
            action.Invoke(options.Limits);
            return options;
        }

        public static bool IsDocker => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

        public static bool IsSslPath(this IConfigurationSection section, string key, out string[] sslpaths)
        {
            var sslpath = section.GetSection(key).GetChildren();
            var strings = sslpath.Select(s => s.Value).ToList();
            if (strings.Count != 0)
            {
                if (strings.Count > 2) throw new Exception($"{key} 参数过多，只支持两个值");
                if (key.Equals("Pfx") && strings.Count < 2) throw new Exception($"{key} 参数过少，只支持两个值");
                strings.Insert(0, key);
                if (strings.Count < 2) strings.Add(string.Empty);
                sslpaths = [.. strings];
                return true;
            }
            sslpaths = null;
            return false;
        }

        public static bool TrySslPath(this IConfigurationSection section, out string[] sslpaths)
        {
            return section.IsSslPath("Pem", out sslpaths) || section.IsSslPath("Pfx", out sslpaths);
        }

        public static List<TlsCipherSuite> GetTlsCiphers(this IConfigurationSection section)
        {
            const string key = "CipherSuites";
            var sslpath = section.GetSection(key).GetChildren();
            var strings = sslpath.Select(s => s.Value).ToList();
            if (strings.Count != 0)
            {
                List<TlsCipherSuite> tlsCipherSuites = [];
                foreach (var txt in strings)
                {
                    if (Enum.TryParse<TlsCipherSuite>(txt, out var result))
                    {
                        tlsCipherSuites.Add(result);
                    }
                    else
                    {
                        throw new Exception($"{key} 配置下：{txt} 不是有效的属性名！");
                    }
                }

                return tlsCipherSuites;
            }
            return [];
        }

        public static SslProtocols GetSslProtocols(this IConfigurationSection section)
        {
            const string key = "SslProtocols";
            var sslpath = section.GetSection(key).GetChildren();
            var strings = sslpath.Select(s => s.Value).ToList();
            if (strings.Count != 0)
            {
                SslProtocols sslProtocols = default;
                foreach (var txt in strings)
                {
                    if (Enum.TryParse<SslProtocols>(txt, out var result))
                    {
                        sslProtocols ^= result;
                    }
                    else
                    {
                        throw new Exception($"{key} 配置下：{txt} 不是有效的属性名！");
                    }
                }

                return sslProtocols;
            }
            return SslProtocols.None;
        }

        public static List<SslApplicationProtocol> GetApplicationProtocols(this IConfigurationSection section)
        {
            const string key = "ApplicationProtocols";
            var sslpath = section.GetSection(key).GetChildren();
            var strings = sslpath.Select(s => s.Value).ToList();
            if (strings.Count != 0)
            {
                List<SslApplicationProtocol> sslApplicationProtocols = [];
                foreach (var txt in strings)
                {
                    var sslApplicationProtocol = txt switch
                    {
                        "Http11" => SslApplicationProtocol.Http11,
                        "Http2" => SslApplicationProtocol.Http2,
                        "Http3" => SslApplicationProtocol.Http3,
                        _ => new SslApplicationProtocol(txt),
                    };
                    sslApplicationProtocols.Add(sslApplicationProtocol);
                }

                return sslApplicationProtocols;
            }
            return [SslApplicationProtocol.Http11, SslApplicationProtocol.Http2, SslApplicationProtocol.Http3];
        }

        public static bool GetConfBool(this IConfigurationSection section, string key, bool defaultValue) => section.GetValue(key, defaultValue);

        public static E GetConfEnum<E>(this IConfigurationSection section, string key, E defaultValue) where E : struct => section.GetValue(key, defaultValue);

        public static string IsAppsettings()
        {
            string configPath = Path.Combine(Environment.CurrentDirectory, "appsettings.json");
            return IsConfigExists(configPath);
        }

        public static string IsDockerAppsettings()
        {
            string dockerConfigPath = Path.Combine(Environment.CurrentDirectory, "config", "appsettings.json");
            return IsConfigExists(dockerConfigPath);
        }

        private static string IsConfigExists(string configPath)
        {
            if (!File.Exists(configPath))
            {
                File.AppendAllText(configPath,
                    """
                    {
                      "Logging": {
                        "LogLevel": {
                          "Default": "Information",
                          "Microsoft": "Warning",
                          "Microsoft.Hosting.Lifetime": "Information",
                          "Yarp.ReverseProxy.Configuration.ConfigProvider.ConfigurationConfigProvider": "Warning"
                        }
                      },
                      "AllowedHosts": "*",
                      //"ServerIp": null, //服务器IP，默认null为不启用，是ClientFrame启用模式
                      "Server.Urls": [ //启动相关服务
                        "https://0.0.0.0:7080"
                      ],
                      "HttpSsl": { //HTTPS相关配置
                      //"nixue.top": {
                      //  "Pfx": [ "certs\\cert.pfx", "certimate" ]
                      //  //"CipherSuites": [ "TLS_AES_256_GCM_SHA384", "TLS_AES_128_GCM_SHA256", "TLS_CHACHA20_POLY1305_SHA256" ], //可以不设置，Win下无效
                      //  //"SslProtocols": [ "Tls", "Tls11", "Tls12", "Tls13" ], //可以不设置
                      //  //"ApplicationProtocols": [ "Http11", "Http2", "Http3" ], //可以不设置
                      //  //"AllowRenegotiation": false, //可以不设置
                      //  //"AllowTlsResume": true, //可以不设置
                      //  //"ClientCertificateRequired": false, //可以不设置
                      //  //"CertificateRevocationCheckMode": "NoCheck" //可以不设置
                      //}
                      },
                      "ReverseProxy": { //代理溯源
                        "Routes": {
                          "route0": {
                            "ClusterId": "test_cluster",
                            "TimeoutPolicy": "5s",
                            "MaxRequestBodySize": -1, //禁用传输限制，字节单位
                            "Match": {
                              "Path": "/a/{**catch-all}",
                              "Hosts": [ "ok.cn" ]
                            },
                            "Transforms": [
                              {
                                "DiyType": "W3CLogger",
                                "Enabled": true,
                                "Level": "Debug",
                                "LogName": "Ok"
                              },
                              {
                                "DiyType": "HttpsRedirect",
                                //"StatusCode": 302,
                                //"Url": "https://127.0.0.1:8888",
                                "Enabled": true
                              },
                              {
                                "DiyType": "Cors",
                                "Enabled": true,
                                "AllowOrigin": "http://example.com",
                                "AllowMethods": "GET,POST",
                                "AllowHeaders": "Content-Type",
                                "AllowCredentials": true
                              },
                              {
                                "DiyType": "StaticFile",
                                "Enabled": true,
                                "RootPath": "wwwroot" // 本地文件夹，相对路径
                              }
                            ]
                          }
                        },
                        "Clusters": {
                          "test_cluster": {
                            "Destinations": {
                              "destination1": {
                                "Address": "http://127.0.0.1:5001"
                              }
                            },
                            "HttpRequest": {
                              "ActivityTimeout": "00:10:00"
                            }
                          }
                        }
                      }
                    }
                    """
                    , Encoding.UTF8);
            }
            return configPath;
        }

        public static void IsCertsPath()
        {
            string certsPath = Path.Combine(Environment.CurrentDirectory, "certs");
            if (!Directory.Exists(certsPath))
            {
                Directory.CreateDirectory(certsPath);
            }
        }

        public static IReverseProxyBuilder LoadDiyFromConfig(this IReverseProxyBuilder builder, IConfiguration config)
        {
            var dynamicConfig = new DynamicConfiguration(config, TimeSpan.FromSeconds(1));
            builder.Services.AddSingleton(dynamicConfig); //动态配置服务，防抖 1 秒
            builder.LoadFromConfig(dynamicConfig.GetSection("ReverseProxy"));

            foreach (var serviceDescriptor in builder.Services)
            {
                if (serviceDescriptor.ServiceType == typeof(IProxyConfigProvider))
                {
                    builder.Services.Remove(serviceDescriptor);
                    var factory = serviceDescriptor.ImplementationFactory;
                    builder.Services.AddSingleton((provider) => OnConfigProvider(provider, factory));
                    break;
                }
            }

            return builder;

            static IProxyConfigProvider OnConfigProvider(IServiceProvider provider, Func<IServiceProvider, object> ImplementationFactory)
            {
                var obj = ImplementationFactory(provider);
                if (obj is IProxyConfigProvider proxyConfig)
                {
                    return new DiyProxyConfigProvider(provider, proxyConfig);
                }
                throw new Exception("无法被实现，不可能！");
            }
        }
    }
}
