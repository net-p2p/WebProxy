using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using WebProxy.Extensions;

namespace WebProxy.DiyTransform.Feature
{
    public enum W3CLevel
    {
        Info,
        Debug
    }

    public class W3CLoggerFeature
    {
        public class DebugW3CLogger
        {
            public string Content { get; init; }
            public object Data { get; init; }
        }

        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

        public string Scheme { get; set; }

        public string Host { get; set; }

        public string Method { get; set; }

        public string Path { get; set; }

        public string Protocol { get; set; }

        public string UserIp { get; set; }

        public string Query { get; set; }

        public DebugW3CLogger RequestContent { get; set; }

        public DebugW3CLogger ResponseContent { get; set; }

        //public string UserAgent { get; set; }

        public async Task ReadRequestContentAsync(HttpContext context, W3CLevel w3CLevel)
        {
            if (w3CLevel is W3CLevel.Debug)
            {
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                var firstReadContent = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
                DebugW3CLogger debug = new() { Content = firstReadContent, Data = context.Request.Headers };
                RequestContent = debug;
            }
        }

        public async Task ReadResponseContentAsync(HttpResponseMessage httpResponse)
        {
            if (httpResponse is null) return;
            var (content, msg) = await ReadAndRecreateHttpContentAsync(httpResponse.Content);
            httpResponse.Content = content;
            ResponseContent = new() { Content = msg, Data = httpResponse };
        }

        public void ReadRequestAppend(StringBuilder builder)
        {
            builder.LogLine().Append("RequestHeaders:");
            if (RequestContent.Data is IHeaderDictionary Header)
            {
                foreach (var (i, item) in Header.Index())
                {
                    builder.Append('[').Append(item.Key).Append(':').Append(item.Value).Append(']');
                    if (i + 1 < Header.Count) builder.Append(',');
                }
            }
            builder.LogLine().Append("RequestBody:").Append(RequestContent.Content);

        }

        public void ReadResponseAppend(StringBuilder builder)
        {
            if (ResponseContent is not null)
            {
                builder.LogLine().Append("ResponseHeaders:");
                if (ResponseContent.Data is HttpResponseMessage httpResponse)
                {

                    if (httpResponse.Headers.Any())
                    {
                        foreach (var (i, item) in httpResponse.Headers.Index())
                        {
                            builder.Append('[').Append(item.Key).Append(':').Append(string.Join(',', item.Value)).Append(']');
                            if (i + 1 < httpResponse.Headers.Count()) builder.Append(',');
                        }
                    }
                    else
                    {
                        builder.Append("N/A");
                    }
                    builder.Append(" <=> ");
                    if (httpResponse.Content.Headers.Any())
                    {
                        foreach (var (i, item) in httpResponse.Content.Headers.Index())
                        {
                            builder.Append('[').Append(item.Key).Append(':').Append(string.Join(',', item.Value)).Append(']');
                            if (i + 1 < httpResponse.Content.Headers.Count()) builder.Append(',');
                        }
                    }
                    else
                    {
                        builder.Append("N/A");
                    }
                }
                builder.LogLine().Append("ResponseBody:").Append(ResponseContent.Content);
            }
            else
            {
                builder.Append('无');
            }
        }

        private static async Task<(HttpContent content, string msg)> ReadAndRecreateHttpContentAsync(HttpContent content)
        {
            if (content is null) return (content, string.Empty);
            using (content)
            {
                MemoryStream memoryStream = new();
                await content.CopyToAsync(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(memoryStream, leaveOpen: true);
                string msg = await reader.ReadToEndAsync();

                memoryStream.Seek(0, SeekOrigin.Begin);
                var newContent = new StreamContent(memoryStream);
                foreach (var header in content.Headers)
                {
                    newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                return (newContent, msg);
            }
        }
    }
}
