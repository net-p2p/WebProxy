using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebProxy.DiyTransform.Validate;
using WebProxy.DiyTransformFactory;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace WebProxy.DiyTransform.Start
{
    public class HttpsRedirectTransformStart : DiyRequestTransform
    {
        private int _statusCode;
        private string _redirectUrl;

        public HttpsRedirectTransformStart(ILogger logger, int statusCode, bool enabled, string redirectUrl) : base(logger, enabled)
        {
            _statusCode = statusCode;
            _redirectUrl = redirectUrl;
        }

        public override ValueTask DiyApplyAsync(RequestTransformContext transformContext)
        {
            var context = transformContext.HttpContext;
            var scheme = context.Request.Scheme;
            var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedProto))
            {
                scheme = forwardedProto;
            }

            if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                var path = transformContext.Path.ToString();
                var query = transformContext.Query.QueryString.ToString();
                var finalUrl = !string.IsNullOrEmpty(_redirectUrl)
                    ? $"{_redirectUrl}{path}{query}"
                    : $"https://{context.Request.Host}{path}{query}";

                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("Redirecting to {Url} with status {StatusCode}", finalUrl, _statusCode);

                context.Response.StatusCode = _statusCode;
                context.Response.Headers.Location = finalUrl;

                return ValueTask.CompletedTask;
            }

            return ValueTask.CompletedTask;
        }

        public override bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig)
        {
            ValidateHttpsRedirect validate = new(_logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                _enabled = false;
                return false;
            }
            else
            {
                _statusCode = validate.StatusCode;
                _enabled = validate.Enabled;
                _redirectUrl = validate.Url;
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{TransformName} updated: Enabled={Enabled}, Url={Url}, StatusCode={StatusCode}", validate.TransformName, _enabled, _redirectUrl, _statusCode);
            }

            return true;
        }

        public static TransformType CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            ValidateHttpsRedirect validate = new(logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                transform = null;
                return TransformType.False;
            }
            transform = new HttpsRedirectTransformStart(factory.CreateLogger(validate.LoggerName), validate.StatusCode, validate.Enabled, validate.Url);
            return TransformType.True;
        }
    }
}
