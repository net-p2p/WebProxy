using System;
using System.Collections.Generic;
using System.Linq;
using Yarp.ReverseProxy.Configuration;

namespace WebProxy.Extensions
{
    public class DiyProxyConfigProvider : IProxyConfigProvider, IDisposable
    {
        private const string fakeName = "fake";
        private readonly ClusterConfig fakeConfig;
        private readonly IProxyConfigProvider oldProxyConfigProvider;
        private readonly IServiceProvider provider;

        public DiyProxyConfigProvider(IServiceProvider provider, IProxyConfigProvider proxyConfigProvider)
        {
            this.provider = provider;
            oldProxyConfigProvider = proxyConfigProvider;
            fakeConfig = new ClusterConfig() { ClusterId = fakeName, Destinations = new Dictionary<string, DestinationConfig> { { "destination", new DestinationConfig() { Address = "file://local" } } } };
        }

        public IProxyConfig GetConfig()
        {
            var proxyConfig = oldProxyConfigProvider.GetConfig();
            if (proxyConfig.Clusters is List<ClusterConfig> clusterConfigs)
            {
                if (!clusterConfigs.Any((c) => c.ClusterId.Equals(fakeName)))
                {
                    clusterConfigs.Add(fakeConfig);
                }
            }
            return proxyConfig;
        }

        void IDisposable.Dispose()
        {
            if (oldProxyConfigProvider is IDisposable disposable) disposable.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
