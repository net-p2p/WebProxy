using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tool.Utils;
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

        private MinDataRate _minRequestDataRate;
        private MinDataRate _minResponseDataRate;

        public BodySizeTransformStart(ILogger logger, bool enabled, long? maxRequestBodySize, MinDataRate minRequestDataRate, MinDataRate minResponseDataRate)
        {
            _logger = logger;
            _enabled = enabled;
            _maxRequestBodySize = maxRequestBodySize;
            _minRequestDataRate = minRequestDataRate;
            _minResponseDataRate = minResponseDataRate;
        }

        public override ValueTask ApplyAsync(RequestTransformContext transformContext)
        {
            if (!_enabled)
            {
                return ValueTask.CompletedTask;
            }

            var context = transformContext.HttpContext;

            if (context.WebSockets.IsWebSocketRequest) return ValueTask.CompletedTask;

            var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySizeFeature is not null && !bodySizeFeature.IsReadOnly)
            {
                bodySizeFeature.MaxRequestBodySize = _maxRequestBodySize;
            }
            else
            {
                _logger.LogWarning("当前中间件（BodySize）无法生效，请移动至 Transforms 首位。");
            }

            if (_minRequestDataRate is not null)
            {
                var requestDataRateFeature = context.Features.Get<IHttpMinRequestBodyDataRateFeature>();
                if (requestDataRateFeature is not null)
                {
                    requestDataRateFeature.MinDataRate = _minRequestDataRate;
                }
                else
                {
                    _logger.LogWarning("当前中间件（BodySize）无法生效，请移动至 Transforms 首位。");
                }
            }

            if (_minResponseDataRate is not null)
            {
                var responseDataRateFeature = context.Features.Get<IHttpMinResponseDataRateFeature>();
                if (responseDataRateFeature is not null)
                {
                    responseDataRateFeature.MinDataRate = _minResponseDataRate;
                }
                else
                {
                    _logger.LogWarning("当前中间件（BodySize）无法生效，请移动至 Transforms 首位。");
                }
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
                _minRequestDataRate = validate.MinRequestDataRate;
                _minResponseDataRate = validate.MinResponseDataRate;

                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{TransformName} updated: Enabled={Enabled}, MaxRequestBodySize={_maxRequestBodySize}, MinRequestDataRate={_minRequestDataRate}, MinResponseDataRate={_minResponseDataRate}", validate.TransformName, _enabled, _maxRequestBodySize, _minRequestDataRate, _minResponseDataRate);
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

            transform = new BodySizeTransformStart(factory.CreateLogger(validate.LoggerName), validate.Enabled, validate.MaxRequestBodySize, validate.MinRequestDataRate, validate.MinResponseDataRate);
            return TransformType.True;
        }
    }
}