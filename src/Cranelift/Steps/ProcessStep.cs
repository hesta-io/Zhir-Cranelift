using Cranelift.Helpers;

using Hangfire;
using Hangfire.Console;
using Hangfire.Server;

using Medallion.Shell;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

using MySql.Data.MySqlClient;

using Renci.SshNet.Common;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Transactions;

namespace Cranelift.Steps
{
    public class WorkerOptions
    {
        public int WorkerCount { get; set; }
        public int ParallelPagesCount { get; set; }
    }

    public class ProcessStep
    {
        public class ProcessResult
        {
            public string Result { get; set; }
            public bool Success { get; set; }
        }

        private readonly IDbContext _dbContext;
        private readonly IStorage _storage;
        private readonly IWebHostEnvironment _environment;
        private readonly PythonHelper _pythonHelper;
        private readonly WorkerOptions _options;

        public ProcessStep(
            IDbContext dbContext,
            IStorage storage,
            IConfiguration configuration,
            IWebHostEnvironment _environment,
            PythonHelper pythonHelper)
        {
            _dbContext = dbContext;
            _storage = storage;
            this._environment = _environment;
            _pythonHelper = pythonHelper;
            _options = configuration.GetSection(Constants.Worker).Get<WorkerOptions>();
        }

        public async Task Execute(string jobId, PerformContext context)
        {
            context.WriteLine($"Processing {jobId}");

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            using (var transaction = await connection.BeginTransactionAsync(
                context.CancellationToken.ShutdownToken))
            {
                // Step 1: Make sure the job is not processed
                var job = await connection.GetJobAsync(jobId);
                if (job.HasFinished())
                {
                    context.WriteLine($"This job is already processed.");
                    return;
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                job.Status = ModelConstants.Processing;
                job.ProcessedAt = DateTime.UtcNow;
                await connection.UpdateJob(job);

                context.CancellationToken.ThrowIfCancellationRequested();

                // Step 2: Download images
                var originalPrefix = $"{Constants.Original}/{job.UserId}/{job.Id}";
                var originalPath = Path.Combine(Path.GetTempPath(), Constants.Cranelift);

                await _storage.DownloadBlobs(originalPrefix, originalPath, context.CancellationToken.ShutdownToken);

                context.CancellationToken.ThrowIfCancellationRequested();

                // TODO: Make sure user has enough balance!

                var pages = Directory.EnumerateFiles(Path.Combine(originalPath, originalPrefix))
                                      .Where(i => IsImage(i))
                                      .Select(i => new Page
                                      {
                                          FullPath = i,
                                          Id = Guid.NewGuid().ToString("N"),
                                          Name = Path.GetFileName(i),
                                          JobId = job.Id,
                                          UserId = job.UserId,
                                          StartedProcessingAt = DateTime.UtcNow,
                                          Deleted = false,
                                          CreatedAt = DateTime.UtcNow,
                                          CreatedBy = job.UserId,
                                      })
                                      .ToArray();

                var parallelizationDegree = _options.ParallelPagesCount;

                var count = 0;
                var chunks = pages.Chunk(parallelizationDegree).ToArray();

                context.CancellationToken.ThrowIfCancellationRequested();

                await connection.DeletePreviousPages(job);

                // Process the pages
                foreach (var chunk in chunks)
                {
                    var tasks = chunk.Select(p => ProcessPage(job, p, context.CancellationToken.ShutdownToken)).ToArray();
                    await Task.WhenAll(tasks);

                    foreach (var task in tasks.Where(t => t.Result.Succeeded == false))
                    {
                        context.WriteLine($"Failed to process page: {task.Result.Name}. Output:\n{task.Result.Result}");
                    }

                    if (tasks.Any(t => t.Result.Succeeded == false))
                    {
                        job.Status = ModelConstants.Failed;
                        job.FailingReason = "Failed to process one or more pages.";
                        break;
                    }
                    else
                    {
                        foreach (var page in chunk)
                        {
                            await connection.InsertPage(page);
                        }

                        count += parallelizationDegree;
                        context.WriteLine($"Progress: {count}/{job.PageCount}");
                    }

                    context.CancellationToken.ThrowIfCancellationRequested();
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                if (job.Status != ModelConstants.Failed)
                {
                    job.Status = ModelConstants.Completed;

                    // TODO: Generate Word/PDF file!
                    var text = string.Join("\n\n\n", pages.Select(p => p.Result));
                    var bytes = Encoding.UTF8.GetBytes(text);
                    var key = $"{Constants.Done}/{job.UserId}/{job.Id}/result.txt";

                    await _storage.UploadBlob(key, new MemoryStream(bytes), "text/plain", context.CancellationToken.ShutdownToken);

                    // TODO: Update balance?
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                // Update job :)
                job.FinishedAt = DateTime.UtcNow;
                await connection.UpdateJob(job);
                await connection.InsertTransaction(new UserTransaction
                {
                    UserId = job.UserId,
                    Amount = -(job.PricePerPage * job.PageCount) ?? 0,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = job.UserId,
                    PaymentMediumId = UserTransaction.PaymentMediums.ZhirBalance,
                    TypeId = UserTransaction.Types.OcrJob,
                });

                await transaction.CommitAsync(context.CancellationToken.ShutdownToken);
            }
        }

        private async Task<Page> ProcessPage(Job job, Page page, System.Threading.CancellationToken cancellationToken)
        {
            var doneKey = $"{Constants.Done}/{job.UserId}/{job.Id}/{page.Name}";
            var donePath = Path.Combine(Path.GetTempPath(), Constants.Cranelift, Constants.Done, job.UserId.ToString(), job.Id, page.Name);

            var success = await Clean(page.FullPath, donePath, cancellationToken);

            if (success)
            {
                var result = await RunTesseract(donePath, cancellationToken, "ckb", "eng");

                if (success)
                {
                    page.Result = result.OutputOrError;
                    // page.FormatedResult
                    success = await _storage.UploadBlob(doneKey, donePath, cancellationToken: cancellationToken);
                }
            }

            page.FinishedProcessingAt = DateTime.UtcNow;
            page.Succeeded = success;

            return page;
        }

        private async Task<bool> Clean(string input, string output, System.Threading.CancellationToken cancellationToken)
        {
            var workingDir = Path.Combine(_environment.ContentRootPath, "Dependencies/zhirpy");
            var scriptPath = Path.Combine(workingDir, "src/clean.py");

            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var result = await _pythonHelper.Run(new[] { scriptPath, input, output }, options =>
            {
                options.CancellationToken(cancellationToken);
                options.WorkingDirectory(workingDir);
            });

            return result.Success;
        }

        private static bool IsImage(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" ||
                   extension == ".png";
        }

        private class TesseractResult
        {
            public bool Success { get; set; }
            public string OutputOrError { get; set; }
        }

        private async Task<TesseractResult> RunTesseract(
            string imageFile, 
            System.Threading.CancellationToken cancellationToken, 
            params string[] languages)
        {
            // The output format depends on the destination extension!
            var tempOutputFile = Path.GetTempFileName() + ".txt";

            try
            {
                var modelsPath = Path.Combine(_environment.ContentRootPath, "Dependencies", "models");

                if (languages is null || languages.Length == 0)
                {
                    languages = Directory.EnumerateFiles(modelsPath, "*.traineddata")
                                      .Select(f => Path.GetFileNameWithoutExtension(f))
                                      .Where(f => f.ToLowerInvariant().StartsWith("ckb"))
                                      .ToArray();
                }
                else if (languages[0].ToLowerInvariant() == "all")
                {
                    languages = Directory.EnumerateFiles(modelsPath, "*.traineddata")
                                      .Select(f => Path.GetFileNameWithoutExtension(f))
                                      .ToArray();
                }

                var workingDir = Path.Combine(_environment.ContentRootPath, "Dependencies/zhirpy");
                var scriptPath = Path.Combine(workingDir, "src/tess.py");

                var langs = string.Join("+", languages);
                var result = await _pythonHelper.Run(
                    new[] { scriptPath, imageFile, tempOutputFile, "--langs", langs },
                    options =>
                {
                    options.WorkingDirectory(workingDir);
                    options.CancellationToken(cancellationToken);

                    options.EnvironmentVariables(new Dictionary<string, string>
                    {
                        { "TESSDATA_PREFIX", modelsPath }
                    });
                });

                if (result.Success)
                {
                    var output = await File.ReadAllTextAsync(tempOutputFile);
                    return new TesseractResult
                    {
                        OutputOrError = output,
                        Success = true
                    };
                }
                else
                {
                    var lines = result.StandardOutput;
                    return new TesseractResult
                    {
                        OutputOrError = string.Join(Environment.NewLine, lines),
                        Success = false
                    };
                }
            }
            finally
            {
                if (File.Exists(tempOutputFile))
                    File.Delete(tempOutputFile);
            }
        }
    }
}
