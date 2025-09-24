using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace WebProxy.Extensions
{
    public static class Exp
    {
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
                      "Server.Urls": [
                        "https://0.0.0.0:7080"
                      ], //启动相关服务
                      "HttpSsl": [
                        //{
                        //  "Domain": "985dw.cn",
                        //  "Pfx": [ "certs\\cert.pfx", "certimate" ]
                        //  "Pem": [ "certs\\certbundle.pem", "certs\\privkey.pem" ]
                        //}
                      ], //HTTPS相关配置
                      "FormOptions": {
                        "BufferBody": false, //完整正文缓冲
                        "MemoryBufferThreshold": 1048576, //缓冲区大小(1M)，超过则写入临时文件
                        "ValueLengthLimit": 4194304, //表单值最大值，默认4M
                        "BufferBodyLengthLimit": 1610612736, //缓冲区最大值，默认30M
                        "MultipartBodyLengthLimit": 1610612736 //最大上传大小，默认128M
                      }, //请求资源限制配置 (覆盖全部参数)
                      "ReverseProxy": { //代理溯源
                        "Routes": {
                          "route0": {
                            "ClusterId": "test_cluster",
                            "TimeoutPolicy": "5s",
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
                                "DiyType": "BodySize",
                                //"MaxRequestBodySize": "5242880",
                                //"MaxRequestBodySize": 19264658,
                                //"MinRequestDataRate": "240,5",
                                //"MinResponseDataRate": "240,5",
                                "Enabled": true
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
                          //"baidu": {
                          //  "LoadBalancingPolicy": "RoundRobin",
                          //  "Destinations": {
                          //    "baidu/destination1": {
                          //      "Address": "https://www.baidu.com/"
                          //    },
                          //    "baidu/destination2": {
                          //      "Address": "https://www.baidu.com/"
                          //    }
                          //  }
                          //},
                          //"Admin_cluster": {
                          //  "Destinations": {
                          //    "Admin_cluster/destination1": {
                          //      "Address": "http://192.168.1.88:8080/"
                          //    }
                          //  }
                          //},
                          //"Risk_cluster": {
                          //  "Destinations": {
                          //    "Risk_cluster/destination1": {
                          //      "Address": "http://192.168.1.88:8083/"
                          //    }
                          //  }
                          //},
                          //"Api_cluster": {
                          //  "Destinations": {
                          //    "Api_cluster/destination1": {
                          //      "Address": "http://192.168.1.88:8081/"
                          //    }
                          //  }
                          //},
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
