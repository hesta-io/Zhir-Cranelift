using Medallion.Shell;

using Microsoft.Extensions.Hosting;

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WebDemo.Services
{
    public class InitializeDependenciesService : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Command.Run("pipenv", new[] { "install" }, options =>
            {
                options.WorkingDirectory(Path.GetFullPath("dependencies/ocr-preprocess"));
            }).Task;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
