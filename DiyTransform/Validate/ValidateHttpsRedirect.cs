using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateHttpsRedirect: ValidateCore
    {
        public string Url { get; }

        public int StatusCode { get; }

        public override bool IsError { get; init; }

        public ValidateHttpsRedirect(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig): base("HttpsRedirectTransform", logger, transformValues, routeConfig)
        {
            if (Enabled
                && ValidateUrl(out string url)
                && ValidateStatusCode(out int statusCode))
            {
                Url = url;
                StatusCode = statusCode;
            }
            else
            {
                IsError = true;
            }
        }

        private bool ValidateUrl(out string url)
        {
            const string Key = "Url";
            if (transformValues.TryGetValue(Key, out var urlValue))
            {
                if (!string.IsNullOrWhiteSpace(urlValue))
                {
                    url = urlValue;
                    return true;
                }
            }
            url = string.Empty;
            return true; ;
        }

        private bool ValidateStatusCode(out int statusCode)
        {
            const string Key = "StatusCode";
            if (transformValues.TryGetValue(Key, out var statusCodeValue))
            {
                if (!int.TryParse(statusCodeValue, out int newStatusCode) || newStatusCode < 300 || newStatusCode > 399)
                {
                    LogError(Key, newStatusCode);
                    statusCode = -1;
                    return false;
                }
                statusCode = newStatusCode;
                return true;
            }
            statusCode = StatusCodes.Status301MovedPermanently;
            return true;
        }

    }
}
