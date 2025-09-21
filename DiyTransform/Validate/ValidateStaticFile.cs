using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateStaticFile : ValidateCore
    {
        public string RootPath { get; }

        public string DefaultFile { get; }

        public string NotFoundPage { get; }

        public string PathPrefix { get; }

        public override bool IsError { get; init; }

        public ValidateStaticFile(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig): base("StaticFileTransform", logger, transformValues, routeConfig)
        {
            if (Enabled
                && ValidateRootPath(out string rootPath)
                && ValidateDefaultFile(out string defaultFile)
                && ValidateNotFoundPage(out string notFoundPage)
                && ValidatePathPrefix(out string pathPrefix))
            {
                RootPath = rootPath;
                DefaultFile = defaultFile;
                NotFoundPage = notFoundPage;
                PathPrefix = pathPrefix;
            }
            else
            {
                IsError = true;
            }
        }

        private bool ValidateRootPath(out string rootPath)
        {
            const string Key = "RootPath";
            if (transformValues.TryGetValue(Key, out var rootPathValue))
            {
                if (!string.IsNullOrWhiteSpace(rootPathValue))
                {
                    rootPath = rootPathValue;
                    return true;
                }
            }
            rootPath = "wwwroot";
            return true;
        }

        private bool ValidateDefaultFile(out string defaultFile)
        {
            const string Key = "DefaultFile";
            if (transformValues.TryGetValue(Key, out var defaultFileValue))
            {
                if (!string.IsNullOrWhiteSpace(defaultFileValue))
                {
                    defaultFile = defaultFileValue;
                    return true;
                }
            }
            defaultFile = "index.html";
            return true;
        }

        private bool ValidateNotFoundPage(out string notFoundPage)
        {
            const string Key = "DefaultFile";
            if (transformValues.TryGetValue(Key, out var notFoundPageValue))
            {
                if (!string.IsNullOrWhiteSpace(notFoundPageValue))
                {
                    notFoundPage = notFoundPageValue;
                    return true;
                }
            }
            notFoundPage = null;
            return true;
        }


        private bool ValidatePathPrefix(out string pathPrefix)
        {
            string routePath = routeConfig?.Match?.Path;
            if (string.IsNullOrEmpty(routePath))
            {
                pathPrefix = "/";
                return true;
            }

            // 查找 {**name} 或 {*name}
            int catchAllIndex = routePath.IndexOf("{**", StringComparison.OrdinalIgnoreCase);
            if (catchAllIndex == -1)
            {
                catchAllIndex = routePath.IndexOf("{*", StringComparison.OrdinalIgnoreCase);
            }

            if (catchAllIndex == -1) 
            {
                pathPrefix = null;
                return false; // 无动态占位符，不适合 StaticFileTransform
            }

            // 提取前缀
            string prefix = routePath.Substring(0, catchAllIndex);
            if (string.IsNullOrEmpty(prefix))
            {
                pathPrefix = "/";
                return true;
            }

            // 确保前缀以 / 开头，末尾添加 /
            if (!prefix.StartsWith('/'))
            {
                prefix = "/" + prefix;
            }
            if (!prefix.EndsWith('/'))
            {
                prefix += "/";
            }

            pathPrefix = prefix;
            return true;
        }
    }
}
