using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Tool;
using Tool.Utils.Data;
using WebProxy.DiyTransform;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace WebProxy.DiyTransformFactory
{
    public enum TransformType
    {
        No,
        True,
        False
    }

    public abstract class DiyRequestTransform : RequestTransform
    {
        public abstract bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig);

        public static TransformType CreateTransform(string typekey, ILogger _logger, ILoggerFactory _loggerFactory, IReadOnlyDictionary<string, string> transformValues, RouteConfig route, out DiyRequestTransform transform)
        {
            return typekey switch
            {
                "BodySize" => BodySizeTransformStart.CreateTransform(_logger, _loggerFactory, transformValues, route, out transform),
                "W3CLogger" => W3CLoggerTransformStart.CreateTransform(_logger, _loggerFactory, transformValues, route, out transform),
                "HttpsRedirect" => HttpsRedirectTransformStart.CreateTransform(_logger, _loggerFactory, transformValues, route, out transform),
                "Cors" => CorsTransformStart.CreateTransform(_logger, _loggerFactory, transformValues, route, out transform),
                "StaticFile" => StaticFileTransformStart.CreateTransform(_logger, _loggerFactory, transformValues, route, out transform),
                "Location" => LocationTransformStart.CreateTransform(_logger, _loggerFactory, transformValues, route, out transform),
                _ => DiyTransformSet.UnknownDiyType(typekey, _logger, out transform),
            };
        }
    }

    public abstract class DiyResponseTransform : ResponseTransform
    {
        public abstract bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig);

        public static TransformType CreateTransform(string typekey, ILogger _logger, ILoggerFactory _loggerFactory, IReadOnlyDictionary<string, string> transformValues, RouteConfig route, out DiyResponseTransform transform)
        {
            return typekey switch
            {
                "W3CLogger" => W3CLoggerTransformEnd.CreateTransform(_logger, _loggerFactory, transformValues, route, out transform),
                "Location" => LocationTransformEnd.CreateTransform(_logger, _loggerFactory, transformValues, route, out transform),
                _ => DiyTransformSet.UnknownDiyType(typekey, _logger, out transform),
            };
        }
    }

    public class DiyTransformSet
    {
        public DiyTransformSet(DiyRequestTransform requestTransform, DiyResponseTransform responseTransform)
        {
            RequestTransform = requestTransform;
            ResponseTransform = responseTransform;
        }

        public DiyRequestTransform RequestTransform { get; }

        public DiyResponseTransform ResponseTransform { get; }

        public bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig route)
        {
            return (RequestTransform?.ResetConf(transformValues, route) ?? true)
                && (ResponseTransform?.ResetConf(transformValues, route) ?? true);
        }

        public void Build(TransformBuilderContext context) 
        {
            // 将 Transform 添加到上下文
            if (RequestTransform is not null) context.RequestTransforms.Add(RequestTransform);
            if (ResponseTransform is not null) context.ResponseTransforms.Add(ResponseTransform);
            //context.ResponseTrailersTransforms.Add();
        }

        public static bool CreateTransform(string typekey, ILogger _logger, ILoggerFactory _loggerFactory, IReadOnlyDictionary<string, string> transformValues, RouteConfig route, out DiyTransformSet transforms)
        {
            if (DiyRequestTransform.CreateTransform(typekey, _logger, _loggerFactory, transformValues, route, out var requestTransform) is not TransformType.False 
                && DiyResponseTransform.CreateTransform(typekey, _logger, _loggerFactory, transformValues, route, out var responseTransform) is not TransformType.False)
            {
                transforms = new(requestTransform, responseTransform);
                return true;
            }
            else
            {
                transforms = null;
                return false;
            }
        }

        public static TransformType UnknownDiyType(string typekey, ILogger _logger, out DiyRequestTransform transform)
        {
            transform = null;
            return UnknownDiyType(typekey, _logger);
        }

        public static TransformType UnknownDiyType(string typekey, ILogger _logger, out DiyResponseTransform transform)
        {
            transform = null;
            return UnknownDiyType(typekey, _logger);
        }

        private static TransformType UnknownDiyType(string typekey, ILogger _logger)
        {
            _logger.LogDebug("Unknown DiyType: {typekey}", typekey);
            return TransformType.No;
        }
    }

    public class DiyTypeTransformFactory(ILoggerFactory _loggerFactory) : ITransformFactory
    {
        private readonly LazyConcurrentDictionary<string, DiyTransformSet> _transformCache = [];

        private readonly ILogger _logger = _loggerFactory.CreateLogger("DiyTypeTransformFactory");

        public bool Validate(TransformRouteValidationContext context, IReadOnlyDictionary<string, string> transformValues)
        {
            if (!transformValues.TryGetValue("DiyType", out var typekey))
            {
                _logger.LogError("Missing DiyType in transform configuration");
                return false;
            }

            string routeId= context.Route.RouteId, clusterId = context.Route.ClusterId;
            string transformKey = $"{routeId}:{clusterId}:{typekey}";
            var route = context.Route;

            if (_transformCache.TryGetValue(transformKey, out var diytransforms))
            {
                return diytransforms.ResetConf(transformValues, route);
            }
            if (DiyTransformSet.CreateTransform(typekey, _logger, _loggerFactory, transformValues, route, out var transformSet))
            {
                _transformCache.Add(transformKey, transformSet);
                return true;
            }
            return false;
        }

        public bool Build(TransformBuilderContext context, IReadOnlyDictionary<string, string> transformValues)
        {
            if (transformValues.TryGetValue("DiyType", out string typekey))
            {
                string routeId = context.Route.RouteId, clusterId = context.Route.ClusterId;
                string transformKey = $"{routeId}:{clusterId}:{typekey}";

                _logger.LogInformation("载入组件 {routeId}.{clusterId}.DiyType:{typekey}", routeId, clusterId, typekey);
                if (_transformCache.TryGetValue(transformKey, out var transformSet))
                {
                    transformSet.Build(context);
                    return true;
                }
            }

            return false;
        }
    }
}
