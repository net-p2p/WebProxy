using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebProxy.DiyTransform.Validate;
using WebProxy.DiyTransformFactory;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace WebProxy.DiyTransform
{
    public class CorsTransformStart : DiyRequestTransform
    {
        private readonly ILogger _logger;
        private bool _enabled;
        private string[] _allowOrigins;
        private string[] _allowMethods;
        private string[] _allowHeaders;
        private bool _allowCredentials;

        public CorsTransformStart(ILogger logger, bool enabled, string[] allowOrigins, string[] allowMethods, string[] allowHeaders, bool allowCredentials)
        {
            _logger = logger;
            _enabled = enabled;
            _allowOrigins = allowOrigins ?? [];
            _allowMethods = allowMethods ?? ["GET", "POST"];
            _allowHeaders = allowHeaders ?? ["*"];
            _allowCredentials = allowCredentials;
        }

        public override ValueTask ApplyAsync(RequestTransformContext transformContext)
        {
            if (!_enabled)
            {
                return ValueTask.CompletedTask;
            }

            var context = transformContext.HttpContext;
            var origin = context.Request.Headers.Origin.FirstOrDefault();

            if (string.IsNullOrEmpty(origin))
            {
                return ValueTask.CompletedTask; // 无 Origin 头，不处理
            }

            // 验证 Origin 是否允许
            bool isOriginAllowed = _allowOrigins.Length == 0 || _allowOrigins.Contains("*") || _allowOrigins.Contains(origin);
            if (!isOriginAllowed)
            {
                _logger.LogDebug("CORS request from {Origin} rejected; allowed origins: {AllowOrigins}", origin, string.Join(",", _allowOrigins));
                return ValueTask.CompletedTask;
            }

            // 添加 CORS 响应头
            context.Response.Headers.AccessControlAllowOrigin = origin;
            if (_allowCredentials)
            {
                context.Response.Headers.AccessControlAllowCredentials = "true";
            }

            context.Response.Headers.AccessControlAllowMethods = string.Join(",", _allowMethods);
            context.Response.Headers.AccessControlAllowHeaders = string.Join(",", _allowHeaders);

            // 处理预检请求（OPTIONS）
            if (context.Request.Method == "OPTIONS")
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                _logger.LogDebug("CORS preflight request from {Origin} handled", origin);
                return ValueTask.CompletedTask;
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
                    _logger.LogError("Invalid Enabled value for CorsTransform: {EnabledValue}", enabledValue);
                    return false;
                }
                _enabled = newEnabled;
                updated = true;
            }

            if (transformValues.TryGetValue("AllowOrigin", out var allowOrigin))
            {
                _allowOrigins = allowOrigin?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
                updated = true;
            }

            if (transformValues.TryGetValue("AllowMethods", out var allowMethods))
            {
                _allowMethods = allowMethods?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
                updated = true;
            }

            if (transformValues.TryGetValue("AllowHeaders", out var allowHeaders))
            {
                _allowHeaders = allowHeaders?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
                updated = true;
            }

            if (transformValues.TryGetValue("AllowCredentials", out var allowCredentials))
            {
                if (!bool.TryParse(allowCredentials, out bool newAllowCredentials))
                {
                    _logger.LogError("Invalid AllowCredentials value for CorsTransform: {AllowCredentials}", allowCredentials);
                    return false;
                }
                _allowCredentials = newAllowCredentials;
                updated = true;
            }

            if (updated)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("CorsTransform updated: Enabled={Enabled}, AllowOrigin={AllowOrigin}, AllowMethods={AllowMethods}, AllowHeaders={AllowHeaders}, AllowCredentials={AllowCredentials}",
                        _enabled, string.Join(",", _allowOrigins), string.Join(",", _allowMethods), string.Join(",", _allowHeaders), _allowCredentials);
                }
            }

            ValidateCors validate = new(_logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                _enabled = false;
                return false;
            }
            else
            {
                _enabled = validate.Enabled;
                _allowOrigins = validate.AllowOrigins;
                _allowMethods = validate.AllowMethods;
                _allowHeaders = validate.AllowHeaders;
                _allowCredentials = validate.AllowCredentials;

                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("CorsTransform updated: Enabled={Enabled}, AllowOrigin={AllowOrigin}, AllowMethods={AllowMethods}, AllowHeaders={AllowHeaders}, AllowCredentials={AllowCredentials}", _enabled, string.Join(",", _allowOrigins), string.Join(",", _allowMethods), string.Join(",", _allowHeaders), _allowCredentials);
            }

            return updated || transformValues.ContainsKey("DiyType");
        }

        public static TransformType CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            ValidateCors validate = new(logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                transform = null;
                return TransformType.False;
            }

            transform = new CorsTransformStart(factory.CreateLogger(validate.LoggerName), validate.Enabled, validate.AllowOrigins, validate.AllowMethods, validate.AllowHeaders, validate.AllowCredentials);
            return TransformType.True;
        }

        private static bool ValidateEnabled(string value, out bool result)
        {
            result = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            return value == null ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}