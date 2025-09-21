using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool.Utils;
using Tool.Web;
using WebProxy.DiyTransform.Feature;
using WebProxy.DiyTransform.Validate;
using WebProxy.DiyTransformFactory;
using WebProxy.Extensions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace WebProxy.DiyTransform
{
    public class W3CLoggerTransformEnd : DiyResponseTransform
    {
        private readonly ILogger _logger;
        private string _logName;
        private bool _enabled;
        private W3CLevel _level;

        private string logPrefix => $"Log/W3CLogger/{_logName}/";

        public W3CLoggerTransformEnd(ILogger logger, bool enabled, W3CLevel level, string logName)
        {
            _logger = logger;
            _enabled = enabled;
            _level = level;
            _logName = logName;
        }

        public override async ValueTask ApplyAsync(ResponseTransformContext transformContext)
        {
            if (_enabled)
            {
                var context = transformContext.HttpContext;

                var loggerFeature = context.Features.Get<W3CLoggerFeature>();
                if (loggerFeature is not null)
                {
                    StringBuilder builder = new();
                    builder.LogAppend(loggerFeature.Method).Append('[').Append(context.TraceIdentifier).LogAppend(']').LogAppend(context.Response.StatusCode)
                        .Append(loggerFeature.Stopwatch.ElapsedMilliseconds).LogAppend("ms").LogAppend(loggerFeature.Scheme).LogAppend(loggerFeature.Host)
                        .Append(loggerFeature.Path).LogAppend(loggerFeature.Query).Append(loggerFeature.UserIp);
                    if (_level is W3CLevel.Debug)
                    {
                        await loggerFeature.ReadResponseContentAsync(transformContext.ProxyResponse);
                        builder.Append(' ').Append(loggerFeature.Protocol);
                        loggerFeature.ReadRequestAppend(builder);
                        loggerFeature.ReadResponseAppend(builder);

                        Log.Debug(builder.ToString(), logPrefix);
                    }
                    else
                    {
                        Log.Info(builder.ToString(), logPrefix);
                    }
                    //string txt = $"{loggerFeature.Stopwatch.ElapsedMilliseconds}ms {loggerFeature.Scheme} {loggerFeature.Host} {loggerFeature.Method} {loggerFeature.Path}{loggerFeature.Query} {loggerFeature.UserIp}";
                    //switch (_level)
                    //{
                    //    case W3CLevel.Debug:
                    //        Log.Debug($"[{context.TraceIdentifier}] {loggerFeature.Protocol} {txt} {loggerFeature.UserAgent} {await W3CLoggerFeature.ReadResponseContentAsync(transformContext.ProxyResponse)}", logPrefix);
                    //        break;
                    //    case W3CLevel.Info:
                    //        Log.Info($"[{context.TraceIdentifier}] {txt}", logPrefix);
                    //        break;
                    //}
                }
            }
        }

        public override bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig)
        {
            ValidateW3CLogger validate = new(_logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                _enabled = false;
                return false;
            }
            else
            {
                _logName = validate.LogName;
                _enabled = validate.Enabled;
                _level = validate.Level;
                if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{TransformName} updated: Enabled={Enabled}, Level={Level}, LogName={LogName}", validate.TransformName, _enabled, _level, _logName);
            }
            return true;
        }

        public static TransformType CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyResponseTransform transform)
        {
            ValidateW3CLogger validate = new(logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                transform = null;
                return TransformType.False;
            }
            transform = new W3CLoggerTransformEnd(factory.CreateLogger(validate.LoggerName), validate.Enabled, validate.Level, validate.LogName);
            return TransformType.True;
        }

    }
}
