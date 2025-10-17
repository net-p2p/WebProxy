using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebProxy.DiyTransform.Validate;
using WebProxy.DiyTransformFactory;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace WebProxy.DiyTransform.Start
{
    public class HeaderDistinctValueTransformStart : DiyRequestTransform
    {
        private HashSet<string> _headers;

        public HeaderDistinctValueTransformStart(ILogger logger, bool enabled, HashSet<string> headers) : base(logger, enabled)
        {
            _headers = headers;
        }

        public override ValueTask DiyApplyAsync(RequestTransformContext transformContext)
        {
            // 处理重复头部（在进入 YARP发起请求 之前）
            var duplicateHeaders = new Dictionary<string, int>();

            var httpRequest = transformContext.ProxyRequest;

            foreach (var header in httpRequest.Headers.ToList())
            {
                if (_headers.Count is 0 || _headers.Contains(header.Key))
                {
                    int count = header.Value.Count();
                    if (count > 1)
                    {
                        var onlys = header.Value.Distinct();
                        if (count != onlys.Count())
                        {
                            httpRequest.Headers.Remove(header.Key);
                            httpRequest.Headers.TryAddWithoutValidation(header.Key, onlys);
                            duplicateHeaders.Add(header.Key, count);
                        }
                    }
                    else if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = header.Value.FirstOrDefault();
                        if (value is not null)
                        {
                            var values = value.Split("; ");
                            var onlys = values.Distinct();
                            if (values.Length != onlys.Count())
                            {
                                httpRequest.Headers.Remove(header.Key);
                                httpRequest.Headers.TryAddWithoutValidation(header.Key, string.Join("; ", onlys));
                                duplicateHeaders.Add(header.Key, values.Length);
                            }
                        }
                    }
                }
            }

            if (_logger.IsEnabled(LogLevel.Debug) && duplicateHeaders.Count != 0)
            {
                _logger.LogDebug("检测到重复头部: {@Headers}", duplicateHeaders);
            }

            return ValueTask.CompletedTask;
        }

        public override bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig)
        {
            ValidateHeaderDistinctValue validate = new(_logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                _enabled = false;
                return false;
            }
            else
            {
                _headers = validate.Headers;
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{TransformName} updated: Enabled={Enabled}, Headers={Headers}", validate.TransformName, _enabled, _headers);
            }

            return true;
        }

        public static TransformType CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            ValidateHeaderDistinctValue validate = new(logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                transform = null;
                return TransformType.False;
            }
            transform = new HeaderDistinctValueTransformStart(factory.CreateLogger(validate.LoggerName), validate.Enabled, validate.Headers);
            return TransformType.True;
        }
    }
}
