using Medallion.Shell;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Services
{
    public class InitializeDependencies : BackgroundService
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<InitializeDependencies> _logger;

        public InitializeDependencies(
            IWebHostEnvironment webHostEnvironment,
            ILogger<InitializeDependencies> logger)
        {
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        protected async override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                var basePath = Path.Combine(_webHostEnvironment.ContentRootPath, "Dependencies");
                var ocrPath = Path.Combine(basePath, "ocr-preprocess");

                _logger.LogInformation("Installing ocr-process dependencies...");

                var command = await InstallDependencies(ocrPath, stoppingToken);

                _logger.LogInformation("poetry: " + Environment.NewLine + command.Result.StandardOutput);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not initialize dependencies.");
            }
        }

        private static async Task<Command> InstallDependencies(string ocrPath, CancellationToken stoppingToken)
        {
            var poetryPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                "poetry.bat" : "poetry";

            var command = Command.Run(poetryPath, new[] { "install" }, options =>
            {
                options.WorkingDirectory(ocrPath);
                options.CancellationToken(stoppingToken);
            });

            await command.Task;

            return command;
        }
    }
}
