using Cranelift.Helpers;

using Medallion.Shell;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Services
{
    public class InitializeDependencies : IHostedService
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly PythonHelper _pythonHelper;
        private readonly ILogger<InitializeDependencies> _logger;

        public InitializeDependencies(
            IWebHostEnvironment webHostEnvironment,
            PythonHelper pythonHelper,
            ILogger<InitializeDependencies> logger)
        {
            _webHostEnvironment = webHostEnvironment;
            _pythonHelper = pythonHelper;
            _logger = logger;
        }

        private async Task<CommandResult> InstallDependencies(string ocrPath, CancellationToken stoppingToken)
        {
            return await _pythonHelper.Run(new[] { "-m", "pip", "install", "-r", "./requirements.txt" }, options =>
            {
                options.WorkingDirectory(ocrPath);
                options.CancellationToken(stoppingToken);
            });
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var workingDir = Path.Combine(_webHostEnvironment.ContentRootPath, "Dependencies", "zhirpy");

            _logger.LogInformation("Installing ocr-process dependencies...");

            var result = await InstallDependencies(workingDir, cancellationToken);

            _logger.LogInformation(result.StandardOutput);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
