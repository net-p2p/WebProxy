using Microsoft.Extensions.Primitives;
using System.Linq;
using System.Threading.Tasks;
using System;
using Yarp.ReverseProxy.Transforms;
using Microsoft.Extensions.Logging;
using WebProxy.DiyTransformFactory;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform
{
    public class HttpsRedirectTransform : DiyRequestTransform
    {
        private readonly ILogger _logger;
        private int _statusCode;
        private bool _enabled;
        private string _redirectUrl;

        public HttpsRedirectTransform(ILogger logger, int statusCode, bool enabled, string redirectUrl)
        {
            _logger = logger;
            _statusCode = statusCode;
            _enabled = enabled;
            _redirectUrl = redirectUrl;
        }

        public override ValueTask ApplyAsync(RequestTransformContext transformContext)
        {
            if (!_enabled)
            {
                return ValueTask.CompletedTask;
            }

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

                _logger.LogDebug("Redirecting to {Url} with status {StatusCode}", finalUrl, _statusCode);

                context.Response.StatusCode = _statusCode;
                context.Response.Headers.Location = finalUrl;

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
                    _logger.LogError("Invalid Enabled value for HttpsRedirectTransform: {EnabledValue}", enabledValue);
                    return false;
                }
                _enabled = newEnabled;
                updated = true;
            }

            if (transformValues.TryGetValue("Url", out var url))
            {
                if (string.IsNullOrEmpty(url))
                {
                    _logger.LogError("Url cannot be empty for HttpsRedirectTransform");
                    return false;
                }
                _redirectUrl = url;
                updated = true;
            }

            if (transformValues.TryGetValue("StatusCode", out var statusCode))
            {
                if (!int.TryParse(statusCode, out int newStatusCode) || newStatusCode < 300 || newStatusCode > 399)
                {
                    _logger.LogError("Invalid StatusCode for HttpsRedirectTransform: {StatusCode}", statusCode);
                    return false;
                }
                _statusCode = newStatusCode;
                updated = true;
            }

            if (updated)
            {
                _logger.LogDebug("HttpsRedirectTransform updated: Enabled={Enabled}, Url={Url}, StatusCode={StatusCode}",
                    _enabled, _redirectUrl, _statusCode);
            }

            return updated || transformValues.ContainsKey("DiyType");
        }

        public static bool CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            bool enabled = true;
            if (transformValues.TryGetValue("Enabled", out var enabledValue) &&
                !ValidateEnabled(enabledValue, out enabled))
            {
                logger.LogError("Invalid Enabled value for HttpsRedirectTransform: {EnabledValue}", enabledValue);
                transform = null;
                return false;
            }

            string url = transformValues.TryGetValue("Url", out var urlValue) ? urlValue : null;
            if (url != null && url.Length == 0)
            {
                logger.LogError("Url is required for HttpsRedirectTransform");
                transform = null;
                return false;
            }

            int statusCode = 301;
            if (transformValues.TryGetValue("StatusCode", out var statusCodeValue))
            {
                if (!int.TryParse(statusCodeValue, out statusCode) || statusCode < 300 || statusCode > 399)
                {
                    logger.LogError("Invalid StatusCode for HttpsRedirectTransform: {StatusCode}", statusCodeValue);
                    transform = null;
                    return false;
                }
            }

            transform = new HttpsRedirectTransform(factory.CreateLogger("HttpsRedirectTransform"), statusCode, enabled, url);
            return true;
        }

        private static bool ValidateEnabled(string enabledValue, out bool enabled)
        {
            enabled = string.Equals(enabledValue, "true", StringComparison.OrdinalIgnoreCase);
            return enabledValue == null ||
                   string.Equals(enabledValue, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(enabledValue, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValidateStatusCode(string statusCodeValue, out int statusCode)
        {
            statusCode = StatusCodes.Status301MovedPermanently;
            return statusCodeValue == null ||
                   (int.TryParse(statusCodeValue, out statusCode) && (statusCode == 301 || statusCode == 302));
        }

        private static bool ValidateUrl(string url)
        {
            return url == null ||
                   (Uri.TryCreate(url, new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = false }, out _));
        }
    }
}
