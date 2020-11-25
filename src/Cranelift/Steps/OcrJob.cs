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

    public class OcrJob
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
        private readonly DocumentHelper _documentHelper;
        private readonly WorkerOptions _options;

        public OcrJob(
            IDbContext dbContext,
            IStorage storage,
            IConfiguration configuration,
            IWebHostEnvironment _environment,
            PythonHelper pythonHelper,
            DocumentHelper pdfHelper)
        {
            _dbContext = dbContext;
            _storage = storage;
            this._environment = _environment;
            _pythonHelper = pythonHelper;
            _documentHelper = pdfHelper;
            _options = configuration.GetSection(Constants.Worker).Get<WorkerOptions>();
        }

        [JobDisplayName("OCR Job ({0})")]
        public async Task ExecuteOcrJob(string jobId, PerformContext context)
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
                await connection.UpdateJobAsync(job);

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

                await connection.DeletePreviousPagesAsync(job);

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
                            await connection.InsertPageAsync(page);
                        }

                        count += parallelizationDegree;
                        context.WriteLine($"Progress: {count}/{job.PageCount}");
                    }

                    context.CancellationToken.ThrowIfCancellationRequested();
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                if (job.Status != ModelConstants.Failed)
                {
                    var folderKey = $"{Constants.Done}/{job.UserId}/{job.Id}";

                    context.WriteLine("Generating text file...");
                    var text = string.Join("\n\n\n", pages.Select(p => p.Result));
                    var textBytes = Encoding.UTF8.GetBytes(text);
                    await _storage.UploadBlob(
                        $"{folderKey}/result.txt",
                        new MemoryStream(textBytes),
                        Constants.PlainText,
                        context.CancellationToken.ShutdownToken);

                    context.WriteLine("Generating hocr file...");
                    var hocr = string.Join("\n\n\n", pages.Select(p => p.HocrResult));
                    var hocrBytes = Encoding.UTF8.GetBytes(hocr);
                    await _storage.UploadBlob(
                        $"{folderKey}/result.hocrlist",
                        new MemoryStream(hocrBytes),
                        Constants.PlainText,
                        context.CancellationToken.ShutdownToken);

                    context.WriteLine("Generating docx file...");
                    var paragraphs = pages.Select(p => HocrParser.Parse(p.HocrResult, p.PredictSizes)).ToArray();
                    var wordDocument = _documentHelper.CreateWordDocument(paragraphs);

                    await _storage.UploadBlob(
                       $"{folderKey}/result.docx",
                       wordDocument,
                       Constants.Docx,
                       context.CancellationToken.ShutdownToken);

                    // TODO: Update balance?
                    job.Status = ModelConstants.Completed;

                    const int minimumNumberOfWordsPerPage = 50;
                    var paidPages = pages.Count(p => p.Result.CountWords() >= minimumNumberOfWordsPerPage);

                    context.WriteLine("Charging for the job...");
                    await connection.InsertTransactionAsync(new UserTransaction
                    {
                        UserId = job.UserId,
                        Amount = -(job.PricePerPage * paidPages) ?? 0,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = job.UserId,
                        PaymentMediumId = UserTransaction.PaymentMediums.ZhirBalance,
                        TypeId = UserTransaction.Types.OcrJob,
                    });
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                // Update job :)
                job.FinishedAt = DateTime.UtcNow;
                await connection.UpdateJobAsync(job);

                await transaction.CommitAsync(context.CancellationToken.ShutdownToken);

                // Clean up temp folder
                Directory.Delete(Path.Combine(originalPath, Constants.Original, job.UserId.ToString(), job.Id), recursive: true);
                Directory.Delete(Path.Combine(Path.GetTempPath(), Constants.Cranelift, Constants.Done, job.UserId.ToString(), job.Id), recursive: true);

                context.WriteLine("Done :)");
            }
        }

        private async Task<Page> ProcessPage(Job job, Page page, System.Threading.CancellationToken cancellationToken)
        {
            var doneKey = $"{Constants.Done}/{job.UserId}/{job.Id}/{page.Name}";
            var donePath = Path.Combine(Path.GetTempPath(), Constants.Cranelift, Constants.Done, job.UserId.ToString(), job.Id, page.Name);

            var cleanResult = await Clean(page.FullPath, donePath, cancellationToken);
            page.Succeeded = cleanResult.Successful;
            page.PredictSizes = !cleanResult.Cleaned;

            if (page.Succeeded)
            {
                var result = await RunTesseract(donePath, cancellationToken, "ckb");
                page.Succeeded = result.Success;

                if (page.Succeeded)
                {
                    page.Result = result.TextOutput;
                    page.HocrResult = result.HocrOutput;
                    // page.FormatedResult
                    page.Succeeded = await _storage.UploadBlob(doneKey, donePath, cancellationToken: cancellationToken);
                }
                else
                {
                    page.Result = result.TextOutput;
                }
            }

            page.FinishedProcessingAt = DateTime.UtcNow;

            return page;
        }

        private class CleanResult
        {
            public bool Successful { get; set; }
            public bool Cleaned { get; set; }
            public string Output { get; set; }
        }

        private async Task<CleanResult> Clean(string input, string output, System.Threading.CancellationToken cancellationToken)
        {
            var workingDir = Path.Combine(_environment.ContentRootPath, "Dependencies/zhirpy");
            var scriptPath = Path.Combine(workingDir, "src/clean.py");

            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var result = await _pythonHelper.Run(new[] { scriptPath, input, output }, options =>
            {
                options.CancellationToken(cancellationToken);
                options.WorkingDirectory(workingDir);
            });

            return new CleanResult
            {
                Successful = result.Success,
                Cleaned = result.StandardOutput.Contains("CLEANED"),
                Output = result.StandardOutput + result.StandardError,
            };
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
            public string TextOutput { get; set; }
            public string HocrOutput { get; set; }
        }

        private async Task<TesseractResult> RunTesseract(
            string imageFile,
            System.Threading.CancellationToken cancellationToken,
            params string[] languages)
        {
            var tempOutputDir = Path.GetTempFileName().Replace(".tmp", "");
            Directory.CreateDirectory(tempOutputDir);

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

                var command = Command.Run("tesseract.exe", new[] { $"-l {langs} {imageFile} {Path.Combine(tempOutputDir, "result")} txt hocr" }, options =>
                {
                    options.WorkingDirectory(workingDir);
                    options.CancellationToken(cancellationToken);

                    options.EnvironmentVariables(new Dictionary<string, string>
                    {
                        { "TESSDATA_PREFIX", modelsPath }
                    });

                    options.StartInfo(info =>
                    {
                        info.Arguments = info.Arguments.Replace("\"", "");
                    });
                });

                await command.Task;

                var result = command.Result;

                if (result.Success)
                {
                    var txt = await File.ReadAllTextAsync(Path.Combine(tempOutputDir, "result.txt"));
                    var hocr = await File.ReadAllTextAsync(Path.Combine(tempOutputDir, "result.hocr"));
                    return new TesseractResult
                    {
                        TextOutput = txt,
                        HocrOutput = hocr,
                        Success = true
                    };
                }
                else
                {
                    var lines = result.StandardError + Environment.NewLine + result.StandardOutput;
                    return new TesseractResult
                    {
                        TextOutput = string.Join(Environment.NewLine, lines),
                        Success = false
                    };
                }
            }
            finally
            {
                if (Directory.Exists(tempOutputDir))
                    Directory.Delete(tempOutputDir, recursive: true);
            }
        }
    }
}
