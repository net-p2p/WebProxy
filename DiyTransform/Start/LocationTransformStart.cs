using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebProxy.DiyTransform.Validate;
using WebProxy.DiyTransformFactory;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace WebProxy.DiyTransform.Start
{
    public class LocationTransformStart : DiyRequestTransform
    {
        private int _statusCode;
        private string _path;
        private string _httpType;

        public LocationTransformStart(ILogger logger, int statusCode, bool enabled, string path, string httpType) : base(logger, enabled)
        {
            _statusCode = statusCode;
            _path = path;
            _httpType = httpType;
        }

        public override ValueTask DiyApplyAsync(RequestTransformContext transformContext)
        {
            if (_httpType.Equals("Request"))
            {
                var context = transformContext.HttpContext;
                if (_path.EndsWith("/{**catch-all}"))
                {
                    var basePath = _path[..^"/{**catch-all}".Length];
                    context.Response.Headers.Location = $"{basePath}{transformContext.Path}{transformContext.Query.QueryString}";
                }
                else if (_path.EndsWith("/{**remainder}"))
                {
                    var basePath = _path[..^"/{**remainder}".Length];
                    context.Response.Headers.Location = $"{basePath}{transformContext.Path}{transformContext.Query.QueryString}";
                }
                else
                {
                    context.Response.Headers.Location = _path;
                }
                context.Response.StatusCode = _statusCode;
            }

            return ValueTask.CompletedTask;
        }

        public override bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig)
        {
            ValidateLocation validate = new(_logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                _enabled = false;
                return false;
            }
            else
            {
                _statusCode = validate.StatusCode;
                _enabled = validate.Enabled;
                _path = validate.Path;
                _httpType = validate.HttpType;
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{TransformName} updated: Enabled={Enabled}, Path={Path}, StatusCode={StatusCode}, HttpType={HttpType}", validate.TransformName, _enabled, _path, _statusCode, _httpType);
            }

            return true;
        }

        public static TransformType CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            ValidateLocation validate = new(logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                transform = null;
                return TransformType.False;
            }
            transform = new LocationTransformStart(factory.CreateLogger(validate.LoggerName), validate.StatusCode, validate.Enabled, validate.Path, validate.HttpType);
            return TransformType.True;
        }
    }
}
