using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Tool.Sockets.Kernels;
using Tool.Sockets.NetFrame;
using Tool.Utils;

namespace WebProxy.Extensions
{
    public static class ServicesCoreExtensions
    {
        #region 暂无调用上层服务的接口

        #endregion
    }

    public interface IServicesCore
    {
        public bool IsStopping { get; }

        public void Stopping();

        public Task StartAsync();

        public ValueTask<NetResponse> SendAsync(ApiPacket api, bool isLog);

        public ValueTask<NetResponse> SendRelayAsync(string IpPort, ApiPacket api, bool isLog);

        public Task StopAsync();
    }

    internal class ServicesCore : IServicesCore
    {
        private readonly string ServerIp;
        private readonly ClientFrame client;
        private readonly ILogger _logger;

        public bool IsStopping { get; set; }

        public bool IsEnable => !string.IsNullOrEmpty(ServerIp);

        public ServicesCore(ILogger<ServicesCore> logger, IConfiguration configuration)
        {
            _logger = logger;
            ServerIp = configuration.GetValue<string>("ServerIp");
            if (IsEnable)
            {
                _logger.LogInformation("服务通讯模式已启用，ServerIp:{ServerIp}", ServerIp);
                client = new ClientFrame(NetBufferSize.Size1024K, true);
                client.AddKeepAlive(30);
                client.SetCompleted(Completed);
            }
            else _logger.LogInformation("服务通讯模式未启用，ServerIp:{ServerIp}", "null");
        }

        void IServicesCore.Stopping()
        {
            // 当应用程序开始关闭时，触发取消
            _logger.LogInformation("代理网关正在退出中...");
            IsStopping = true;
        }

        Task IServicesCore.StartAsync()
        {
            return client?.ConnectAsync(ServerIp, 8081) ?? Task.CompletedTask;
        }

        Task IServicesCore.StopAsync()
        {
            return client is null ? Task.CompletedTask : Task.Run(client.Close);
        }

        async ValueTask<NetResponse> IServicesCore.SendAsync(ApiPacket api, bool isLog)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await client.SendAsync(api);
            SetLog("Send", api, in response, stopwatch, isLog);
            return response;
        }

        async ValueTask<NetResponse> IServicesCore.SendRelayAsync(string IpPort, ApiPacket api, bool isLog)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await client.SendRelayAsync(IpPort, api);
            SetLog("SendRelay", api, in response, stopwatch, isLog);
            return response;
        }

        private void SetLog(string name, ApiPacket api, in NetResponse response, Stopwatch stopwatch, bool isLog)
        {
            if (isLog)
            {
                if (!api.TryGet("key", out string val)) val = "无";
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("{name}:{ClassID}.{ActionID} State:{State} Reply:{Reply} {ms:F4}ms Content:{val} ResultLength:{Length}", name, api.ClassID, api.ActionID, response.State, response.IsReply, stopwatch.ElapsedMilliseconds(), val, response.Text?.Length ?? 0);
            }
        }

        private async ValueTask Completed(UserKey ip, EnClient en, DateTime time)
        {
            switch (en)
            {
                case EnClient.Connect:
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("连接服务成功！[{ip}]\t{time:yyyy/MM/dd HH:mm:ss:fffffff}", ip, time);
                    var api = new ApiPacket(0, 0);
                    const string name = "proxy";
                    api.Set("serverName", name);
                    api.Set("fakeIp", Exp.EncodeAsIpPort(name, null));
                    _ = client.SendAsync(api).AsTask().ContinueWith(async (task) =>
                    {
                        using var netResponse = await task;
                        if (_logger.IsEnabled(LogLevel.Information))
                            _logger.LogInformation("{ip}", netResponse.Text);
                    });
                    break;
                case EnClient.Fail:
                case EnClient.Close:
                    if (en is EnClient.Fail)
                    {
                        _logger.LogInformation("网络异常，连接服务器失败！");
                    }
                    else
                    {
                        _logger.LogInformation("网络异常，与服务器连接中断！");
                        //keep?.Close();
                    }
                    break;
                case EnClient.Reconnect:
                    _logger.LogInformation("服务器繁忙，正在重连中···");
                    break;
                default:
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("IP:{ip} \t{en} \t{time:yyyy/MM/dd HH:mm:ss:ffff}", ip, en, time);
                    break;
            }
            await ValueTask.CompletedTask;
        }
    }
}
