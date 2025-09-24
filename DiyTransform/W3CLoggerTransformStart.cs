using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Threading.Tasks;
using Tool.Utils;
using Tool.Web;
using WebProxy.DiyTransform.Feature;
using WebProxy.DiyTransform.Validate;
using WebProxy.DiyTransformFactory;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace WebProxy.DiyTransform
{
    public class W3CLoggerTransformStart : DiyRequestTransform
    {
        private readonly ILogger _logger;
        private string _logName;
        private bool _enabled;
        private W3CLevel _level;

        //private string logPrefix => $"Log/W3CLogger/{_logName}/";

        public W3CLoggerTransformStart(ILogger logger, bool enabled, W3CLevel level, string logName)
        {
            _logger = logger;
            _enabled = enabled;
            _level = level;
            _logName = logName;
        }

        public override async ValueTask ApplyAsync(RequestTransformContext transformContext)
        {
            if (_enabled)
            {
                var context = transformContext.HttpContext;

                var (scheme, host) = context.GetSchemeHost();
                var loggerFeature = new W3CLoggerFeature()
                {
                    Scheme = scheme,
                    Host = host,
                    Method = context.Request.Method,
                    Path = context.Request.Path,
                    Protocol = context.Request.Protocol,
                    UserIp = context.GetUserIp(),
                    Query = context.Request.QueryString.Value ?? string.Empty,
                    //UserAgent = context.Request.Headers.TryGetValue("User-Agent", out var value) ? value : "N/A"
                };
                await loggerFeature.ReadRequestContentAsync(context, _level);
                context.Features.Set(loggerFeature);

                //string txt = $"{scheme} {host} {loggerFeature.Method} {loggerFeature.Path}{loggerFeature.Query} {loggerFeature.UserIp}";
                //switch (_level)
                //{
                //    case W3CLevel.Debug:
                //        Log.Debug($"Start[{context.TraceIdentifier}] {loggerFeature.Protocol} {txt} {loggerFeature.UserAgent} {await W3CLoggerFeature.ReadRequestContentAsync(context)}", logPrefix);
                //        break;
                //    case W3CLevel.Info:
                //        Log.Info($"Start[{context.TraceIdentifier}] {txt}", logPrefix);
                //        break;
                //}
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

        public static TransformType CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            ValidateW3CLogger validate = new(logger, transformValues, routeConfig);
            if (validate.IsError)
            {
                transform = null;
                return TransformType.False;
            }
            transform = new W3CLoggerTransformStart(factory.CreateLogger(validate.LoggerName), validate.Enabled, validate.Level, validate.LogName);
            return TransformType.True;
        }
    }
}
