using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using Yarp.ReverseProxy.Transforms;
using System.Linq;
using Tool.Utils;
using Tool.Web;
using WebProxy.DiyTransformFactory;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform
{
    public class W3CLoggerTransform : DiyRequestTransform
    {
        private readonly ILogger _logger;
        private readonly string _clusterId;
        private bool _enabled;
        private W3CLevel _level;

        public enum W3CLevel 
        {
            Info,
            Debug
        }

        public W3CLoggerTransform(ILogger logger, bool enabled, string clusterId, W3CLevel level)
        {
            _logger = logger;
            _enabled = enabled;
            _clusterId = clusterId;
            _level = level;
        }

        public override ValueTask ApplyAsync(RequestTransformContext transformContext)
        {
            if (_enabled)
            {
                var context = transformContext.HttpContext;

                var (scheme, host) = context.GetSchemeHost();
                string method = context.Request.Method, 
                    path = context.Request.Path, 
                    protocol = context.Request.Protocol, 
                    ip = context.GetUserIp(), 
                    query = context.Request.QueryString.Value ?? string.Empty,
                    userAgent = context.Request.Headers.TryGetValue("User-Agent", out var value) ? value : "无";
                
                switch (_level)
                {
                    case W3CLevel.Debug:
                        Log.Debug($"{scheme} {host} {method} {path} {protocol}{query} {ip} {userAgent}", $"Log/W3CLogger/{_clusterId}/");
                        break;
                    case W3CLevel.Info:
                        Log.Info($"{scheme} {host} {method} {path}{query} {ip}", $"Log/W3CLogger/{_clusterId}/");
                        break;
                }
            }

            return ValueTask.CompletedTask;
        }

        public override bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig)
        {
            bool updated = false;

            if (transformValues.TryGetValue("Enabled", out var enabledValue))
            {
                if (!ValidateEnabled(enabledValue, out bool newEnabled))
                {
                    _logger.LogError("Invalid Enabled value for W3CLoggerTransform: {EnabledValue}", enabledValue);
                    return false;
                }
                _enabled = newEnabled;
                updated = true;
            }

            if (transformValues.TryGetValue("Level", out var levelValue))
            {
                if (!ValidateLevel(levelValue, out W3CLevel newLevel))
                {
                    _logger.LogError("Invalid Level value for W3CLoggerTransform: {LevelValue}", levelValue);
                    return false;
                }
                _level = newLevel;
                updated = true;
            }

            if (updated)
            {
                _logger.LogDebug("W3CLoggerTransform updated: Enabled={Enabled}, Level={Level}", _enabled, _level);
            }

            return updated || transformValues.ContainsKey("DiyType");
        }

        public static bool CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            string clusterId = routeConfig.ClusterId;
            bool enabled = true;
            if (transformValues.TryGetValue("Enabled", out var enabledValue) &&
                !ValidateEnabled(enabledValue, out enabled))
            {
                logger.LogError("Invalid Enabled value for W3CLoggerTransform: {EnabledValue}", enabledValue);
                transform = null;
                return false;
            }

            W3CLevel level = W3CLevel.Info;
            if (transformValues.TryGetValue("Level", out var levelValue) &&
                !ValidateLevel(levelValue, out level))
            {
                logger.LogError("Invalid Level value for W3CLoggerTransform: {LevelValue}", levelValue);
                transform = null;
                return false;
            }

            transform = new W3CLoggerTransform(factory.CreateLogger($"W3CLoggerTransform:{clusterId}"), enabled, clusterId, level);
            return true;
        }

        private static bool ValidateEnabled(string enabledValue, out bool enabled)
        {
            enabled = string.Equals(enabledValue, "true", StringComparison.OrdinalIgnoreCase);
            return enabledValue == null ||
                   string.Equals(enabledValue, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(enabledValue, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValidateLevel(string levelValue, out W3CLevel level)
        {
            if (string.IsNullOrEmpty(levelValue))
            {
                level = W3CLevel.Info;
                return true;
            }

            if (string.Equals(levelValue, "Debug", StringComparison.OrdinalIgnoreCase))
            {
                level = W3CLevel.Debug;
                return true;
            }

            if (string.Equals(levelValue, "Info", StringComparison.OrdinalIgnoreCase))
            {
                level = W3CLevel.Info;
                return true;
            }

            level = W3CLevel.Info;
            return false;
        }
    }
}
