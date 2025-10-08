using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace WebProxy.Extensions
{
    public class FirstService : IHostedService
    {
        readonly IServicesCore _servicesCore;
        readonly IHostApplicationLifetime _hostApplicationLifetime;

        public FirstService(IServicesCore servicesCore, IHostApplicationLifetime hostApplicationLifetime)
        {
            _servicesCore = servicesCore;
            _hostApplicationLifetime = hostApplicationLifetime;
            _hostApplicationLifetime.ApplicationStopping.Register(OnStopping);
        }

        private void OnStopping() => _servicesCore.Stopping();

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _servicesCore.StartAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _servicesCore.StopAsync();
        }
    }
}
