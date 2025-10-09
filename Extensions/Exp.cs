using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;
using static System.Net.Mime.MediaTypeNames;

namespace WebProxy.Extensions
{
    public static class Exp
    {
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

        public static string IsDockerAppsettings()
        {
            string dockerConfigPath = Path.Combine(Environment.CurrentDirectory, "config", "appsettings.json");
#if DOCKER
            if (!File.Exists(dockerConfigPath)) 
            {
                File.AppendAllText(dockerConfigPath,
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
#endif
            return dockerConfigPath;
        }

    }
}
