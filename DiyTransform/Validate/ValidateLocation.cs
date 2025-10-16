using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateLocation : ValidateCore
    {
        public string Path { get; }

        public int StatusCode { get; }

        public string HttpType { get; }

        public override bool IsError { get; init; }

        public ValidateLocation(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig): base("LocationTransform", logger, transformValues, routeConfig)
        {
            if (Enabled)
            {
                if (ValidatePath(out string path)
                    && ValidateStatusCode(out int statusCode)
                    && ValidateHttpType(out string httpType))
                {
                    Path = path;
                    StatusCode = statusCode;
                    HttpType = httpType;
                }
                else
                {
                    IsError = true;
                }
            }
        }

        private bool ValidatePath(out string path)
        {
            const string Key = "Path";
            if (transformValues.TryGetValue(Key, out var pathValue))
            {
                if (!string.IsNullOrWhiteSpace(pathValue))
                {
                    path = pathValue;
                    return true;
                }
            }
            path = string.Empty;
            return true;
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

        private bool ValidateHttpType(out string httpType)
        {
            const string Key = "HttpType";
            if (transformValues.TryGetValue(Key, out var statusHttpType))
            {
                switch (statusHttpType)
                {
                    case "Request":
                        httpType = "Request";
                        return true;
                    case "Response":
                        httpType = "Response";
                        return true;
                    default:
                        LogError(Key, "not is Request or Response.");
                        httpType = string.Empty;
                        return false;
                }
            }
            httpType = "Request";
            return true;
        }
    }
}
