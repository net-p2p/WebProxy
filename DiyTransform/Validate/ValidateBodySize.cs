using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateBodySize : ValidateCore
    {
        public long? MaxRequestBodySize { get; }

        public override bool IsError { get; init; }

        public ValidateBodySize(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig) : base("BodySizeTransform", logger, transformValues, routeConfig)
        {
            if (Enabled)
            {
                if (ValidateMaxRequestBodySize(out long? maxRequestBodySize))
                {
                    MaxRequestBodySize = maxRequestBodySize;
                }
                else
                {
                    IsError = true;
                }
            }
        }

        private bool ValidateMaxRequestBodySize(out long? maxRequestBodySize)
        {
            const string Key = "MaxRequestBodySize";
            if (transformValues.TryGetValue(Key, out var maxRequestBodySizeValue) && !string.IsNullOrEmpty(maxRequestBodySizeValue))
            {
                if (long.TryParse(maxRequestBodySizeValue, out long newMaxRequestBodySize))
                {
                    if (newMaxRequestBodySize >= 5242880) //30,000,000
                    {
                        maxRequestBodySize = newMaxRequestBodySize;
                        return true;
                    }
                    LogError(Key, "包体最小必须大于 5,242,880 字节。");
                    maxRequestBodySize = -1;
                    return false;
                }
                else
                {
                    LogError(Key, "输入值非整数数字。");
                    maxRequestBodySize = -1;
                    return false;
                }
            }
            maxRequestBodySize = null;    // 100MB
            return true;
        }

    }
}
