using Cranelift.Helpers;

using Hangfire;
using Hangfire.Console;
using Hangfire.Server;

using Medallion.Shell;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

using Renci.SshNet.Common;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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
        private readonly WorkerOptions _options;

        public ProcessStep(
            IDbContext dbContext,
            IStorage storage,
            IConfiguration configuration,
            IWebHostEnvironment _environment)
        {
            _dbContext = dbContext;
            _storage = storage;
            this._environment = _environment;
            _options = configuration.GetSection(Constants.Worker).Get<WorkerOptions>();
        }

        [JobDisplayName("Processing job {0}")]
        public async Task Execute(string jobId, PerformContext context)
        {

            context.WriteLine($"Processing {jobId}");

            using var connection = await _dbContext.OpenConnectionAsync(Constants.OcrConnectionName);

            // Step 1: Make sure the job is not processed
            var job = await connection.GetJobAsync(jobId);
            if (job.Status != ModelConstants.Queued)
            {
                context.WriteLine($"This job is not in the 'queued' status. It might already be processed, or it's not ready to be processed yet.");
                return;
            }

            try
            {

                job.Status = ModelConstants.Processing;
                job.ProcessedAt = DateTime.UtcNow;
                await connection.UpdateJob(job);

                // Step 2: Download images
                var originalPrefix = $"{Constants.Original}/{job.UserId}/{job.Id}";
                var originalPath = Path.Combine(Path.GetTempPath(), Constants.Cranelift);

                await _storage.DownloadBlobs(originalPrefix, originalPath);

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

                var tasks = pages.Select(p => ProcessPage(job, p, connection)).ToArray();

                var count = 0;
                var chunks = tasks.Chunk(parallelizationDegree).ToArray();

                // Process the pages
                foreach (var chunk in chunks)
                {
                    await Task.WhenAll(chunk);

                    foreach (var page in chunk.Where(t => t.Result.Succeeded == false))
                    {
                        context.WriteLine($"Failed to process page: {page.Result.Name}. Output:\n{page.Result.Result}");
                    }

                    if (chunk.Any(t => t.Result.Succeeded == false))
                    {
                        job.Status = ModelConstants.Failed;
                        job.FailingReason = "Failed to process one or more pages.";
                        break;
                    }
                    else
                    {
                        count += parallelizationDegree;
                        context.WriteLine($"Progress: {count}/{job.PageCount}");
                    }
                }

                // TODO: Generate Word/PDF file!

                if (job.Status != ModelConstants.Failed)
                {
                    job.Status = ModelConstants.Completed;
                }

                // Update job :)
                job.FinishedAt = DateTime.UtcNow;
                await connection.UpdateJob(job);
            }
            catch (Exception ex)
            {
                job.Status = ModelConstants.Queued;
                await connection.UpdateJob(job);
                throw;
            }
        }

        private async Task<Page> ProcessPage(Job job, Page page, DbConnection connection)
        {
            var doneKey = $"{Constants.Done}/{job.UserId}/{job.Id}/{page.Name}";
            var donePath = Path.Combine(Path.GetTempPath(), Constants.Cranelift, Constants.Done, job.UserId.ToString(), job.Id, page.Name);

            var success = await Preprocess(page.FullPath, donePath);

            if (success)
            {
                var result = await RunTesseract(donePath, "ckb", "eng");

                if (success)
                {
                    page.Result = result.OutputOrError;
                    // page.FormatedResult
                    success = await _storage.UploadBlob(doneKey, donePath);
                }
            }

            page.FinishedProcessingAt = DateTime.UtcNow;
            page.Succeeded = success;

            await connection.InsertPage(page);

            return page;
        }

        private async Task<bool> Preprocess(string input, string output)
        {
            var preProcessPath = Path.Combine(_environment.ContentRootPath, "Dependencies/ocr-preprocess");
            var scriptPath = Path.Combine(preProcessPath, "src/pre-process.py");
            var poetryPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                "poetry.bat" : "poetry";

            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var command = Command.Run(poetryPath, new[] { "run", "python", scriptPath, "-i", input, "-o", output }, options =>
            {
                options.WorkingDirectory(preProcessPath);
            });

            await command.Task;

            return command.Result.Success;
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

        private async Task<TesseractResult> RunTesseract(string imageFile, params string[] languages)
        {
            var tempOutputFile = Path.GetTempFileName();
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

            try
            {
                var arguments = new[] { imageFile, tempOutputFile, "-l", string.Join("+", languages) };

                var command = Command.Run("tesseract", arguments, options =>
                {
                    options.EnvironmentVariables(new Dictionary<string, string>
                    {
                        { "TESSDATA_PREFIX", modelsPath }
                    });
                });

                await command.Task; // Wait for the process to exit

                tempOutputFile += ".txt"; // tesseract adds .txt at the end of the filename!
                if (command.Result.Success)
                {
                    var output = File.ReadAllText(tempOutputFile);
                    return new TesseractResult
                    {
                        OutputOrError = output,
                        Success = true
                    };
                }
                else
                {
                    var lines = command.GetOutputAndErrorLines();
                    return new TesseractResult
                    {
                        OutputOrError = string.Join(Environment.NewLine, lines),
                        Success = false
                    };
                }
            }
            finally
            {
                File.Delete(tempOutputFile);
            }
        }
    }
}
