using Cranelift.Helpers;

using Hangfire;
using Hangfire.Console;
using Hangfire.Server;

using Medallion.Shell;

using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace Cranelift.Steps
{
    public class WorkerOptions
    {
        public int WorkerCount { get; set; }
        public int ParallelPagesCount { get; set; }
    }

    public class ProcessStep
    {
        private readonly IDbContext _dbContext;
        private readonly IStorage _storage;
        private readonly WorkerOptions _options;

        public ProcessStep(
            IDbContext dbContext,
            IStorage storage,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _storage = storage;
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

            // Step 2: Download images
            var originalPrefix = $"{Constants.Cranelift}/{Constants.Original}/{job.UserId}/{job.Id}";
            var originalPath = Path.Combine(Path.GetTempPath(), Constants.Original);

            await _storage.DownloadBlobs(originalPrefix, originalPath);

            var pages = Directory.EnumerateFiles(originalPath)
                                  .Where(i => IsImage(i))
                                  .Select(i => new Page
                                  {
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

            var tasks = pages.Select(p => ProcessPage(job, p, connection));

            foreach (var chunk in tasks.Chunk(parallelizationDegree))
            {
                await Task.WhenAll(chunk);
            }

            //var insertSql = connection.InsertPages(images, jobId);


            // LOOP A:
            // Step 3: Pre-process images
            // Step 4: Process images
            // Step 5: Save results
            // Step 6: Go back to LOOP A until all images are processed

            // Step 7: Update job status
        }

        private Task ProcessPage(Job job, Page page, DbConnection connection)
        {
            var donePrefix = $"{Constants.Cranelift}/{Constants.Done}/{job.UserId}/{job.Id}";
            var processedPath = Path.Combine(Path.GetTempPath(), Constants.Original);

            throw new NotImplementedException();
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

        private static TesseractResult RunTesseract(string tesseractPath, string imageFile, params string[] languages)
        {
            tesseractPath = Path.GetFullPath(tesseractPath);

            var tempOutputFile = Path.GetTempFileName();
            var modelsPath = Path.Combine(tesseractPath, "tessdata");

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

                var command = Command.Run(Path.Combine(tesseractPath, "tesseract.exe"), arguments, options =>
                {
                    options.EnvironmentVariables(new Dictionary<string, string>
                    {
                        { "TESSDATA_PREFIX", Path.Combine(tesseractPath, "tessdata") }
                    });
                });

                command.Wait(); // Wait for the process to exit

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
