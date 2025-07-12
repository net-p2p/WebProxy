using System.Collections.Generic;
using System;
using WebProxy.DiyTransform;
using Yarp.ReverseProxy.Transforms.Builder;
using Yarp.ReverseProxy.Transforms;
using Tool;
using Microsoft.Extensions.Logging;
using Tool.Utils.Data;
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransformFactory
{
    public abstract class DiyRequestTransform : RequestTransform
    {
        /// <summary>
        /// Updates the transform configuration based on the provided values.
        /// </summary>
        /// <param name="transformValues">The new configuration values.</param>
        /// <param name="routeConfig"></param>
        /// <returns>True if the configuration was updated successfully; otherwise, false.</returns>
        public abstract bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig);
    }

    public class DiyTypeTransformFactory(ILoggerFactory _loggerFactory) : ITransformFactory
    {
        private readonly LazyConcurrentDictionary<string, DiyRequestTransform> _transformCache = [];
        private readonly ILogger _logger = _loggerFactory.CreateLogger("DiyTypeTransformFactory");

        public bool Validate(TransformRouteValidationContext context, IReadOnlyDictionary<string, string> transformValues)
        {
            if (!transformValues.TryGetValue("DiyType", out var typekey))
            {
                _logger.LogError("Missing DiyType in transform configuration");
                return false;
            }

            string clusterId = context.Route.ClusterId;
            string transformKey = $"{clusterId}:{typekey}";
            var route = context.Route;

            if (_transformCache.TryGetValue(transformKey, out var diytransform))
            {
                return diytransform.ResetConf(transformValues, route);
            }
            else
            {
                switch (typekey)
                {
                    case "W3CLogger":
                        {
                            if (W3CLoggerTransform.CreateTransform(_logger, _loggerFactory, transformValues, route, out var transform))
                            {
                                _transformCache.Add(transformKey, transform);
                                return true;
                            }
                        }
                        return false;
                    case "HttpsRedirect":
                        {
                            if (HttpsRedirectTransform.CreateTransform(_logger, _loggerFactory, transformValues, route, out var transform))
                            {
                                _transformCache.Add(transformKey, transform);
                                return true;
                            }
                        }
                        return false;
                    case "Cors":
                        if (CorsTransform.CreateTransform(_logger, _loggerFactory, transformValues, route, out var corsTransform))
                        {
                            _transformCache.Add(transformKey, corsTransform);
                            return true;
                        }
                        return false;
                    case "StaticFile":
                        if (StaticFileTransform.CreateTransform(_logger, _loggerFactory, transformValues, route, out var staticTransform))
                        {
                            _transformCache.Add(transformKey, staticTransform);
                            return true;
                        }
                        return false;
                    default:
                        _logger.LogError("Unknown DiyType: {typekey}", typekey);
                        return false;
                }
            }
        }

        public bool Build(TransformBuilderContext context, IReadOnlyDictionary<string, string> transformValues)
        {
            if (transformValues.TryGetValue("DiyType", out string typekey))
            {
                string clusterId = context.Route.ClusterId;
                string transformKey = $"{clusterId}:{typekey}";

                _logger.LogInformation("载入组件 {clusterId}.DiyType: {typekey}", clusterId, typekey);
                if (_transformCache.TryGetValue(transformKey, out var transform))
                {
                    // 将 Transform 添加到上下文
                    context.RequestTransforms.Add(transform);

                    //context.ResponseTrailersTransforms.Add();
                    //context.ResponseTransforms.Add();
                    return true;
                }
            }

            return false;
        }
    }
}
