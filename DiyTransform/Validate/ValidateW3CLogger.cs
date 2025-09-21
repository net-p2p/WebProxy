using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WebProxy.DiyTransform.Feature;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.DiyTransform.Validate
{
    public class ValidateW3CLogger : ValidateCore
    {
        public string LogName { get; }

        public W3CLevel Level { get; }

        public override bool IsError { get; init; }

        public ValidateW3CLogger(ILogger logger, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig): base("W3CLoggerTransform", logger, transformValues, routeConfig)
        {
            if (Enabled
                && ValidateLevel(out W3CLevel level)
                && ValidateLogName(out string logName))
            {
                Level = level;
                LogName = logName;
            }
            else
            {
                IsError = true;
            }
        }

        private bool ValidateLevel(out W3CLevel level)
        {
            const string Key = "Level";
            if (transformValues.TryGetValue(Key, out var levelValue) && !string.IsNullOrEmpty(levelValue))
            {
                if (string.Equals(levelValue, "Debug", StringComparison.OrdinalIgnoreCase))
                {
                    level = W3CLevel.Debug;
                    return true;
                }

                if (string.Equals(levelValue, "Info", StringComparison.OrdinalIgnoreCase))
                {
                    level = W3CLevel.Info;
                    return true;
                }

                level = default;
                LogError(Key, levelValue);
                return false;
            }
            level = W3CLevel.Info;
            return true;
        }

        private bool ValidateLogName(out string logName)
        {
            const string Key = "LogName";
            if (transformValues.TryGetValue(Key, out var logNameValue) && !string.IsNullOrEmpty(logNameValue))
            {
                if (IsValidFileName(logNameValue))
                {
                    logName = logNameValue;
                    return true;
                }
                logName = null;
                LogError(Key, logNameValue);
                return false;
            }
            logName = ClusterId;
            return true;

            bool IsValidFileName(string logNameValue)
            {
                if (string.IsNullOrWhiteSpace(logNameValue))
                {
                    logger.LogError("文件名不能为空或只包含空白字符。");
                    return false;
                }
                char[] invalidChars = Path.GetInvalidFileNameChars();
                if (logNameValue.Any(c => invalidChars.Contains(c)))
                {
                    logger.LogError("文件名 '{logNameValue}' 包含非法字符。不允许的字符有: {invalidChars}", logNameValue, string.Join("", invalidChars));
                    return false;
                }
                if (logNameValue.Length > 255)
                {
                    logger.LogError("文件名 '{logNameValue}' 长度超过255个字符，可能非法。", logNameValue);
                    return false;
                }
                return true;
            }
        }
    }
}
