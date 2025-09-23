using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateCors : ValidateCore
    {
        public string[] AllowOrigins { get; }

        public string[] AllowMethods { get; }

        public string[] AllowHeaders { get; }

        public bool AllowCredentials { get; }

        public override bool IsError { get; init; }

        public ValidateCors(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig) : base("CorsTransform", logger, transformValues, routeConfig)
        {
            if (Enabled)
            {
                if (ValidateAllowOrigins(out string[] allowOrigins)
                    && ValidateAllowMethods(out string[] allowMethods)
                    && ValidateAllowHeaders(out string[] allowHeaders)
                    && ValidateAllowCredentials(out bool allowCredentials))
                {
                    AllowOrigins = allowOrigins;
                    AllowMethods = allowMethods;
                    AllowHeaders = allowHeaders;
                    AllowCredentials = allowCredentials;
                }
                else
                {
                    IsError = true;
                }
            }
        }

        private bool ValidateAllowOrigins(out string[] allowOrigins)
        {
             allowOrigins = transformValues.TryGetValue("AllowOrigin", out var allowOriginValue)
                ? allowOriginValue?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [] : [];
            return true;
        }

        private bool ValidateAllowMethods(out string[] allowMethods)
        {
            allowMethods = transformValues.TryGetValue("AllowMethods", out var allowMethodsValue)
               ? allowMethodsValue?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [] : [];
            return true;
        }

        private bool ValidateAllowHeaders(out string[] allowHeaders)
        {
            allowHeaders = transformValues.TryGetValue("AllowHeaders", out var allowHeadersValue)
                ? allowHeadersValue?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [] : [];
            return true;
        }

        private bool ValidateAllowCredentials(out bool allowCredentials)
        {
            const string Key = "AllowCredentials";
            if (transformValues.TryGetValue(Key, out var allowCredentialsValue))
            {
                if (!bool.TryParse(allowCredentialsValue, out allowCredentials))
                {
                    LogError(Key, allowCredentialsValue);
                    return false;
                }
                return true;
            }
            allowCredentials = false;
            return true;
        }
    }
}
