using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebProxy.DiyTransform.Validate;
using WebProxy.DiyTransformFactory;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace WebProxy.DiyTransform
{
    public class LocationTransformEnd : DiyResponseTransform
    {
        private readonly ILogger _logger;
        private int _statusCode;
        private bool _enabled;
        private string _redirectHost;

        public LocationTransformEnd(ILogger logger, int statusCode, bool enabled, string redirectHost)
        {
            _logger = logger;
            _statusCode = statusCode;
            _enabled = enabled;
            _redirectHost = redirectHost;
        }

        public override ValueTask ApplyAsync(ResponseTransformContext transformContext)
        {
            if (!_enabled)
            {
                return ValueTask.CompletedTask;
            }

            var context = transformContext.HttpContext;
            if (context.Response.Headers.TryGetValue(HeaderNames.Location, out var location))
            {
                if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
                {
                    string scheme = context.Request.Scheme,
                           host = string.IsNullOrEmpty(_redirectHost) ? context.Request.Host.Value : _redirectHost;
                    var newUri = $"{scheme}://{host}{uri.PathAndQuery}{uri.Fragment}";

                    context.Response.Headers.Location = newUri;
                    if (_statusCode > 0) context.Response.StatusCode = _statusCode;
                }
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
                _redirectHost = validate.Host;
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{TransformName} updated: Enabled={Enabled}, Url={Url}, StatusCode={StatusCode}", validate.TransformName, _enabled, _redirectHost, _statusCode);
            }

            return true;
        }

        public static TransformType CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyResponseTransform transform)
        {
            ValidateLocation validate = new(logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                transform = null;
                return TransformType.False;
            }
            transform = new LocationTransformEnd(factory.CreateLogger(validate.LoggerName), validate.StatusCode, validate.Enabled, validate.Host);
            return TransformType.True;
        }
    }
}
