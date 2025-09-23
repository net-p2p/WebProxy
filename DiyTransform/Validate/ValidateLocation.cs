using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateLocation : ValidateCore
    {
        public string Host { get; }

        public int StatusCode { get; }

        public override bool IsError { get; init; }

        public ValidateLocation(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig): base("LocationTransform", logger, transformValues, routeConfig)
        {
            if (Enabled)
            {
                if (ValidateHost(out string host)
                    && ValidateStatusCode(out int statusCode))
                {
                    Host = host;
                    StatusCode = statusCode;
                }
                else
                {
                    IsError = true;
                }
            }
        }

        private bool ValidateHost(out string url)
        {
            const string Key = "Host";
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
            statusCode = 0;
            return true;
        }

    }
}
