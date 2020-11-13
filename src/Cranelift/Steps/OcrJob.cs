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

                    context.WriteLine("Generating pdf file...");
                    var pdfBytes = _documentHelper.MergePages(pages.Select(p => p.PdfResult).ToList());
                    await _storage.UploadBlob(
                        $"{folderKey}/result.pdf",
                        new MemoryStream(pdfBytes),
                        Constants.Pdf,
                        context.CancellationToken.ShutdownToken);

                    context.WriteLine("Generating text file...");
                    var text = string.Join("\n\n\n", pages.Select(p => p.Result));
                    var textBytes = Encoding.UTF8.GetBytes(text);
                    await _storage.UploadBlob(
                        $"{folderKey}/result.txt", 
                        new MemoryStream(textBytes), 
                        Constants.PlainText, 
                        context.CancellationToken.ShutdownToken);

                    context.WriteLine("Generating docx file...");
                    var wordDocument = _documentHelper.CreateWordDocument(pages.Select(p => p.Result).ToArray());
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
                context.WriteLine("Done :)");
            }
        }

        private async Task<Page> ProcessPage(Job job, Page page, System.Threading.CancellationToken cancellationToken)
        {
            var doneKey = $"{Constants.Done}/{job.UserId}/{job.Id}/{page.Name}";
            var donePath = Path.Combine(Path.GetTempPath(), Constants.Cranelift, Constants.Done, job.UserId.ToString(), job.Id, page.Name);

            page.Succeeded = await Clean(page.FullPath, donePath, cancellationToken);

            if (page.Succeeded)
            {
                var result = await RunTesseract(donePath, cancellationToken, "ckb", "ara");
                page.Succeeded = result.Success;

                if (page.Succeeded)
                {
                    page.Result = result.TextOutput;
                    page.PdfResult = result.PdfOutput;
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
            public string TextOutput { get; set; }
            public byte[] PdfOutput { get; internal set; }
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
                var result = await _pythonHelper.Run(
                    new[] { scriptPath, imageFile, tempOutputDir, "--langs", langs },
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
                    var output = await File.ReadAllTextAsync(Path.Combine(tempOutputDir, "result.txt"));
                    return new TesseractResult
                    {
                        TextOutput = output,
                        PdfOutput = await File.ReadAllBytesAsync(Path.Combine(tempOutputDir, "result.pdf")),
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
