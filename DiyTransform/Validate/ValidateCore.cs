using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public abstract class ValidateCore
    {
        protected readonly RouteConfig routeConfig;
        protected readonly IReadOnlyDictionary<string, string> transformValues;
        protected readonly ILogger logger;

        public string TransformName { get; }

        public string RouteId => routeConfig.RouteId;

        public string ClusterId  => routeConfig.ClusterId;

        public string LoggerName => $"{TransformName}:{RouteId}:{ClusterId}";

        public bool Enabled { get; }

        public abstract bool IsError { get; init; }

        public ValidateCore(string TransformName, ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig)
        {
            this.routeConfig = routeConfig ?? throw new ArgumentNullException(nameof(routeConfig));
            this.transformValues = transformValues ?? new Dictionary<string, string>();
            this.TransformName = TransformName;
            this.logger = logger;

            if (ValidateEnabled(out bool enabled))
            {
                Enabled = enabled;
            }
            else
            {
                IsError = true;
            }
        }

        private bool ValidateEnabled(out bool enabled)
        {
            const string Key = "Enabled";
            if (transformValues.TryGetValue(Key, out var enabledValue))
            {
                enabled = string.Equals(enabledValue, "true", StringComparison.OrdinalIgnoreCase);
                bool isok = enabledValue == null ||
                       string.Equals(enabledValue, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(enabledValue, "false", StringComparison.OrdinalIgnoreCase);

                if (!isok) LogError(Key, enabledValue);
                return isok;
            }
            enabled = true;
            return true;
        }

        protected void LogError(string Key, object val) 
        {
            logger.LogError("Invalid {Key} for {Transform}: {val}", Key, TransformName, val);
        }
    }
}
