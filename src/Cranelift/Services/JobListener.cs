using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Services
{
    public class ListenerOptions
    {
        public double IntervalSeconds { get; set; }
    }

    public class JobListener : IHostedService
    {
        public JobListener(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double waitSeconds = 1;
                using (var scope = ServiceProvider.CreateScope())
                {
                    ListenerOptions options = GetOptions(scope);

                    waitSeconds = options.IntervalSeconds;
                }

                await Task.Delay((int)(waitSeconds * 1000));
            }
        }

        private static ListenerOptions GetOptions(IServiceScope scope)
        {
            var configs = scope.ServiceProvider.GetService<IConfiguration>();
            var options = new ListenerOptions();
            configs.GetSection("Listener").Bind(options);
            return options;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
