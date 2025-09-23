using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebProxy.DiyTransform.Validate;
using WebProxy.DiyTransformFactory;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace WebProxy.DiyTransform
{
    public class BodySizeTransformStart : DiyRequestTransform
    {
        private readonly ILogger _logger;
        private bool _enabled;
        private long? _maxRequestBodySize;

        public BodySizeTransformStart(ILogger logger, bool enabled, long? maxRequestBodySize)
        {
            _logger = logger;
            _enabled = enabled;
            _maxRequestBodySize = maxRequestBodySize;
        }

        public override ValueTask ApplyAsync(RequestTransformContext transformContext)
        {
            if (!_enabled)
            {
                return ValueTask.CompletedTask;
            }

            var context = transformContext.HttpContext;
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature != null && !feature.IsReadOnly)
            {
                feature.MaxRequestBodySize = _maxRequestBodySize;
            }
            else
            {
                _logger.LogWarning("当前中间件（BodySize）无法生效，请移动至 Transforms 首位。");
            }

            return ValueTask.CompletedTask;
        }

        public override bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig)
        {
            ValidateBodySize validate = new(_logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                _enabled = false;
                return false;
            }
            else
            {
                _enabled = validate.Enabled;
                _maxRequestBodySize = validate.MaxRequestBodySize;

                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{TransformName} updated: Enabled={Enabled}, MaxRequestBodySize={_maxRequestBodySize}", validate.TransformName, _enabled, _maxRequestBodySize);
            }
            return true;
        }

        public static TransformType CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            ValidateBodySize validate = new(logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                transform = null;
                return TransformType.False;
            }

            transform = new BodySizeTransformStart(factory.CreateLogger(validate.LoggerName), validate.Enabled, validate.MaxRequestBodySize);
            return TransformType.True;
        }
    }
}