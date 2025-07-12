using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Tool.Sockets.Kernels;
using WebProxy.DiyTransformFactory;

namespace WebProxy.DiyTransform
{
    public class StaticFileTransform : DiyRequestTransform
    {
        private readonly ILogger _logger;
        private bool _enabled;
        private string _rootPath;
        private string _defaultFile;
        private string _pathPrefix;
        private string _notFoundPage; // 新增字段：404页面

        public StaticFileTransform(ILogger logger, bool enabled, string rootPath, string defaultFile, string pathPrefix, string notFoundPage)
        {
            _logger = logger;
            _enabled = enabled;
            _rootPath = string.IsNullOrEmpty(rootPath) ? "wwwroot" : rootPath;
            _defaultFile = string.IsNullOrEmpty(defaultFile) ? "index.html" : defaultFile;
            _pathPrefix = pathPrefix ?? "/";
            _notFoundPage = notFoundPage; // 初始化404页面
        }

        public override async ValueTask ApplyAsync(RequestTransformContext transformContext)
        {
            if (!_enabled)
            {
                return;
            }

            var context = transformContext.HttpContext;
            var requestPath = context.Request.Path.Value;

            // 移除路由前缀
            string relativePath = _pathPrefix == "/"
                ? requestPath.TrimStart('/')
                : requestPath.StartsWith(_pathPrefix, StringComparison.OrdinalIgnoreCase)
                    ? requestPath[_pathPrefix.Length..]
                    : requestPath.TrimStart('/');

            // 构建文件路径
            string filePath = Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

            // 安全检查：防止路径穿越
            if (!filePath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Invalid file path: {FilePath}", filePath);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            try
            {
                // 检查是否为文件
                if (File.Exists(filePath))
                {
                    await ServeFileAsync(context, filePath);
                    return;
                }

                // 检查是否为目录或请求以 / 结尾
                if (Directory.Exists(filePath) || relativePath.EndsWith('/'))
                {
                    // 尝试服务默认文件
                    string defaultFilePath = Path.Combine(filePath, _defaultFile);
                    if (File.Exists(defaultFilePath))
                    {
                        await ServeFileAsync(context, defaultFilePath);
                        return;
                    }
                    _logger.LogDebug("Default file not found: {DefaultFilePath}", defaultFilePath);
                }

                // 文件和默认文件都不存在，尝试返回404页面
                if (!string.IsNullOrEmpty(_notFoundPage))
                {
                    string notFoundFilePath = Path.Combine(_rootPath, _notFoundPage.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(notFoundFilePath))
                    {
                        await ServeFileAsync(context, notFoundFilePath);
                        _logger.LogDebug("Serving not found page: {NotFoundFilePath} for request: {FilePath}", notFoundFilePath, filePath);
                        return;
                    }
                    _logger.LogDebug("Not found page not available: {NotFoundFilePath}", notFoundFilePath);
                }

                // 如果没有配置404页面或页面不存在，返回404
                _logger.LogDebug("File not found: {FilePath}", filePath);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving file: {FilePath}", filePath);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }

        private async Task ServeFileAsync(HttpContext context, string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            var contentType = GetContentType(filePath);
            long fileLength = fileInfo.Length;

            // 生成 ETag
            string etag = $"\"{fileInfo.LastWriteTime.Ticks}\"";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.AcceptRanges = "bytes";

            // 检查条件请求（If-None-Match）
            if (context.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch))
            {
                if (ifNoneMatch == etag)
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    _logger.LogDebug("File not modified: {FilePath}, ETag: {ETag}", filePath, etag);
                    return;
                }
            }

            // 检查 Range 头
            if (context.Request.Headers.TryGetValue("Range", out var rangeHeader))
            {
                // 解析 Range: bytes=start-end,...
                var range = rangeHeader.ToString();
                if (!range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                    context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                    _logger.LogWarning("Invalid Range header format: {RangeHeader}", rangeHeader);
                    return;
                }

                var rangeValue = range.Substring("bytes=".Length).Trim();
                if (string.IsNullOrEmpty(rangeValue))
                {
                    context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                    context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                    _logger.LogWarning("Empty range value: {RangeHeader}", rangeHeader);
                    return;
                }

                // 解析多个范围
                var ranges = new List<(long start, long end)>();
                foreach (var rangeSpec in rangeValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (rangeSpec.StartsWith('-'))
                    {
                        // 后缀字节：bytes=-500
                        if (!long.TryParse(rangeSpec.AsSpan(1), out var suffixLength) || suffixLength <= 0)
                        {
                            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                            context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                            _logger.LogWarning("Invalid suffix range: {RangeSpec}", rangeSpec);
                            return;
                        }
                        long start = Math.Max(0, fileLength - suffixLength);
                        long end = fileLength - 1;
                        ranges.Add((start, end));
                    }
                    else
                    {
                        var parts = rangeSpec.Split('-');
                        if (parts.Length != 2)
                        {
                            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                            context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                            _logger.LogWarning("Invalid range format: {RangeSpec}", rangeSpec);
                            return;
                        }

                        if (!long.TryParse(parts[0], out long start) || start < 0)
                        {
                            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                            context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                            _logger.LogWarning("Invalid start value: {RangeSpec}", rangeSpec);
                            return;
                        }

                        if (start >= fileLength)
                        {
                            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                            context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                            _logger.LogWarning("Start out of bounds: {Start} >= {FileLength}", start, fileLength);
                            return;
                        }

                        if (string.IsNullOrEmpty(parts[1]))
                        {
                            long end = fileLength - 1;
                            ranges.Add((start, end));
                        }
                        else if (!long.TryParse(parts[1], out long end))
                        {
                            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                            context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                            _logger.LogWarning("Invalid end value: {RangeSpec}", rangeSpec);
                            return;
                        }
                        else if (end < start || end >= fileLength)
                        {
                            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                            context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                            _logger.LogWarning("Invalid end value: {End}, start: {Start}, fileLength: {FileLength}", end, start, fileLength);
                            return;
                        }
                        else
                        {
                            ranges.Add((start, end));
                        }
                    }
                }

                if (ranges.Count == 0)
                {
                    context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                    context.Response.Headers.ContentRange = $"bytes */{fileLength}";
                    _logger.LogWarning("No valid ranges: {RangeHeader}", rangeHeader);
                    return;
                }

                // 单范围：206 Partial Content
                if (ranges.Count == 1)
                {
                    var (start, end) = ranges[0];
                    context.Response.StatusCode = StatusCodes.Status206PartialContent;
                    context.Response.ContentLength = end - start + 1;
                    context.Response.Headers.ContentRange = $"bytes {start}-{end}/{fileLength}";

                    _logger.LogDebug("Serving partial file: {FilePath}, Content-Type: {ContentType}, DefaultFile: {DefaultFile}, PathPrefix: {PathPrefix}, Range: {Range}, ETag: {ETag}",
                        filePath, contentType, _defaultFile, _pathPrefix, rangeHeader, etag);

                    await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);
                    fileStream.Seek(start, SeekOrigin.Begin);
                    await CopyRangeAsync(fileStream, context.Response.Body, end - start + 1, _logger);
                }
                else
                {
                    // 多范围：multipart/byteranges
                    string boundary = $"boundary_{Guid.NewGuid().ToString("N")}";
                    context.Response.StatusCode = StatusCodes.Status206PartialContent;
                    context.Response.ContentType = $"multipart/byteranges; boundary={boundary}";

                    _logger.LogDebug("Serving multi-range file: {FilePath}, Content-Type: {ContentType}, DefaultFile: {DefaultFile}, PathPrefix: {PathPrefix}, Range: {Range}, ETag: {ETag}",
                        filePath, contentType, _defaultFile, _pathPrefix, rangeHeader, etag);

                    await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);
                    foreach (var (start, end) in ranges)
                    {
                        // 写入分隔符和头部
                        string partHeader = $"--{boundary}\r\n" +
                                            $"Content-Type: {contentType}\r\n" +
                                            $"Content-Range: bytes {start}-{end}/{fileLength}\r\n" +
                                            "\r\n";
                        byte[] partHeaderBytes = Encoding.ASCII.GetBytes(partHeader);
                        await context.Response.Body.WriteAsync(partHeaderBytes);

                        // 写入范围内容
                        fileStream.Seek(start, SeekOrigin.Begin);
                        await CopyRangeAsync(fileStream, context.Response.Body, end - start + 1, _logger);
                    }

                    // 写入结束分隔符
                    string endBoundary = $"\r\n--{boundary}--\r\n";
                    byte[] endBoundaryBytes = Encoding.ASCII.GetBytes(endBoundary);
                    await context.Response.Body.WriteAsync(endBoundaryBytes);
                }
            }
            else
            {
                // 完整文件
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentLength = fileLength;
                context.Response.ContentType = contentType;

                _logger.LogDebug("Serving file: {FilePath}, Content-Type: {ContentType}, DefaultFile: {DefaultFile}, PathPrefix: {PathPrefix}, ETag: {ETag}",
                    filePath, contentType, _defaultFile, _pathPrefix, etag);

                await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);
                await fileStream.CopyToAsync(context.Response.Body);
            }
        }

        private static async Task CopyRangeAsync(FileStream source, Stream destination, long length, ILogger logger)
        {
            const int BufferSize = 65536; // 64KB 缓冲区
            await using var buffer = new BytesCore(BufferSize);
            long remaining = length;
            long totalRead = 0;

            while (remaining > 0)
            {
                int bytesToRead = (int)Math.Min(buffer.Length, remaining);
                int read = await source.ReadAsync(buffer.Memory[..bytesToRead]);
                if (read == 0)
                {
                    logger.LogWarning("Unexpected end of file stream while copying range, remaining: {Remaining}", remaining);
                    break; // 文件意外结束
                }

                await destination.WriteAsync(buffer.Memory[..read]);
                remaining -= read;
                totalRead += read;

                logger.LogTrace("Read and wrote {Read} bytes, remaining: {Remaining}", read, remaining);
            }

            if (remaining > 0)
            {
                logger.LogDebug("Copied {Bytes} bytes, less than requested {Length}", totalRead, length);
            }
            else
            {
                logger.LogDebug("Copied {Bytes} bytes as requested", totalRead);
            }
        }

        public override bool ResetConf(IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig)
        {
            bool updated = false;

            if (transformValues.TryGetValue("Enabled", out var enabledValue))
            {
                if (!ValidateEnabled(enabledValue, out bool newEnabled))
                {
                    _logger.LogError("Invalid Enabled value for StaticFileTransform: {EnabledValue}", enabledValue);
                    return false;
                }
                _enabled = newEnabled;
                updated = true;
            }

            if (transformValues.TryGetValue("RootPath", out var rootPath))
            {
                if (string.IsNullOrEmpty(rootPath))
                {
                    _logger.LogError("RootPath cannot be empty for StaticFileTransform");
                    return false;
                }
                _rootPath = rootPath;
                updated = true;
            }

            if (transformValues.TryGetValue("DefaultFile", out var defaultFile))
            {
                _defaultFile = string.IsNullOrEmpty(defaultFile) ? "index.html" : defaultFile;
                updated = true;
            }

            if (transformValues.TryGetValue("NotFoundPage", out var notFoundPage))
            {
                _notFoundPage = notFoundPage; // 支持动态更新NotFoundPage
                updated = true;
            }

            // 更新前缀
            string newPathPrefix = ExtractPathPrefix(routeConfig?.Match?.Path);
            if (newPathPrefix == null)
            {
                _logger.LogError("Invalid Match.Path for StaticFileTransform: {RoutePath}", routeConfig?.Match?.Path);
                return false;
            }
            if (_pathPrefix != newPathPrefix)
            {
                _pathPrefix = newPathPrefix;
                updated = true;
            }

            if (updated)
            {
                _logger.LogDebug("StaticFileTransform updated: Enabled={Enabled}, RootPath={RootPath}, DefaultFile={DefaultFile}, PathPrefix={PathPrefix}, NotFoundPage={NotFoundPage}",
                    _enabled, _rootPath, _defaultFile, _pathPrefix, _notFoundPage);
            }

            return updated || transformValues.ContainsKey("DiyType");
        }

        public static bool CreateTransform(ILogger logger, ILoggerFactory factory, IReadOnlyDictionary<string, string> transformValues, RouteConfig routeConfig, out DiyRequestTransform transform)
        {
            bool enabled = true;
            if (transformValues.TryGetValue("Enabled", out var enabledValue) &&
                !ValidateEnabled(enabledValue, out enabled))
            {
                logger.LogError("Invalid Enabled value for StaticFileTransform: {EnabledValue}", enabledValue);
                transform = null;
                return false;
            }

            string rootPath = "wwwroot";
            if (transformValues.TryGetValue("RootPath", out var rootPathValue))
            {
                if (string.IsNullOrEmpty(rootPathValue))
                {
                    logger.LogError("RootPath cannot be empty for StaticFileTransform");
                    transform = null;
                    return false;
                }
                rootPath = rootPathValue;
            }

            string defaultFile = "index.html";
            if (transformValues.TryGetValue("DefaultFile", out var defaultFileValue))
            {
                defaultFile = string.IsNullOrEmpty(defaultFileValue) ? "index.html" : defaultFileValue;
            }

            string notFoundPage = null; // 默认无404页面
            if (transformValues.TryGetValue("NotFoundPage", out var notFoundPageValue))
            {
                notFoundPage = notFoundPageValue;
            }

            // 从 RouteConfig 提取前缀
            string pathPrefix = ExtractPathPrefix(routeConfig?.Match?.Path);
            if (pathPrefix == null)
            {
                logger.LogError("Invalid Match.Path for StaticFileTransform: {RoutePath}", routeConfig?.Match?.Path);
                transform = null;
                return false;
            }

            transform = new StaticFileTransform(factory.CreateLogger("StaticFileTransform"), enabled, rootPath, defaultFile, pathPrefix, notFoundPage);
            return true;
        }

        private static bool ValidateEnabled(string value, out bool result)
        {
            result = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            return value == null ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".jpg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".json" => "application/json",
                _ => "application/octet-stream"
            };
        }

        private static string ExtractPathPrefix(string routePath)
        {
            if (string.IsNullOrEmpty(routePath))
            {
                return "/";
            }

            // 查找 {**name} 或 {*name}
            int catchAllIndex = routePath.IndexOf("{**", StringComparison.OrdinalIgnoreCase);
            if (catchAllIndex == -1)
            {
                catchAllIndex = routePath.IndexOf("{*", StringComparison.OrdinalIgnoreCase);
            }

            if (catchAllIndex == -1)
            {
                return null; // 无动态占位符，不适合 StaticFileTransform
            }

            // 提取前缀
            string prefix = routePath.Substring(0, catchAllIndex);
            if (string.IsNullOrEmpty(prefix))
            {
                return "/";
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

            return prefix;
        }
    }
}