using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateBodySize : ValidateCore
    {
        public long? MaxRequestBodySize { get; }

        public MinDataRate MinRequestDataRate { get; }

        public MinDataRate MinResponseDataRate { get; }

        public override bool IsError { get; init; }

        public ValidateBodySize(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig) : base("BodySizeTransform", logger, transformValues, routeConfig)
        {
            if (Enabled)
            {
                if (ValidateMaxRequestBodySize(out long? maxRequestBodySize)
                    && ValidateMinRequestDataRate(out MinDataRate minRequestDataRate)
                    && ValidateMinResponseDataRate(out MinDataRate minResponseDataRate))
                {
                    MaxRequestBodySize = maxRequestBodySize;
                    MinRequestDataRate = minRequestDataRate;
                    MinResponseDataRate = minResponseDataRate;
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

        private bool ValidateMinRequestDataRate(out MinDataRate minRequestDataRate)
        {
            const string Key = "MinRequestDataRate";
            if (transformValues.TryGetValue(Key, out var minRequestDataRateValue) && !string.IsNullOrEmpty(minRequestDataRateValue))
            {
                int[] result = ParseNumberPair(minRequestDataRateValue);
                if (result is not null)
                {
                    minRequestDataRate = new MinDataRate(result[0], TimeSpan.FromSeconds(result[1]));
                    return true;
                }
                else
                {
                    LogError(Key, "格式错误，必须是：[240,5]格式。");
                    minRequestDataRate = null;
                    return false;
                }
            }
            minRequestDataRate = null;
            return true;
        }

        private bool ValidateMinResponseDataRate(out MinDataRate minResponseDataRate)
        {
            const string Key = "MinResponseDataRate";
            if (transformValues.TryGetValue(Key, out var minResponseDataRateValue) && !string.IsNullOrEmpty(minResponseDataRateValue))
            {
                int[] result = ParseNumberPair(minResponseDataRateValue);
                if (result is not null)
                {
                    minResponseDataRate = new MinDataRate(result[0], TimeSpan.FromSeconds(result[1]));
                    return true;
                }
                else
                {
                    LogError(Key, "格式错误，必须是：[240,5]格式。");
                    minResponseDataRate = null;
                    return false;
                }
            }
            minResponseDataRate = null;
            return true;
        }

        public static int[] ParseNumberPair(string input)
        {
            string pattern = @"^(\d+),(\d+)$";
            Match match = Regex.Match(input, pattern);

            if (match.Success)
            {
                int num1 = int.Parse(match.Groups[1].Value);
                int num2 = int.Parse(match.Groups[2].Value);
                return [num1, num2];
            }

            return null; // 或抛出异常
        }
    }
}
