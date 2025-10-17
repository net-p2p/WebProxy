using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateHeaderDistinctValue : ValidateCore
    {
        public HashSet<string> Headers { get; }

        public override bool IsError { get; init; }

        public ValidateHeaderDistinctValue(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig) : base("HeaderDistinctValueTransform", logger, transformValues, routeConfig)
        {
            if (Enabled)
            {
                if (ValidateHeaders(out var headers))
                {
                    Headers = headers;
                }
                else
                {
                    IsError = true;
                }
            }
        }

        private bool ValidateHeaders(out HashSet<string> headers)
        {
            const string Key = "Headers";
            if (transformValues.TryGetValue(Key, out var headersValue))
            {
                if (!string.IsNullOrWhiteSpace(headersValue))
                {
                    headers = new HashSet<string>(headersValue.Split(','), StringComparer.OrdinalIgnoreCase);
                    return true;
                }
            }
            headers = [];
            return true;
        }
    }
}
