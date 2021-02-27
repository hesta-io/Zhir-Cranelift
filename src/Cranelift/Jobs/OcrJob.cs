using Cranelift.Helpers;

using Hangfire;
using Hangfire.Console;
using Hangfire.Server;

using Medallion.Shell;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Cranelift.Common.Models;
using Cranelift.Common.Abstractions;

namespace Cranelift.Jobs
{
    public class WebhookDto
    {
        public string id { get; set; }
        public string name { get; set; }
        public string code { get; set; }
        public int user_id { get; set; }
        public int page_count { get; set; }
        public int paid_page_count { get; set; }
        public string user_failing_reason { get; set; }
        public string status { get; set; }
        public string lang { get; set; }
        public DateTime queued_at { get; set; }
        public DateTime processed_at { get; set; }
        public DateTime finished_at { get; set; }
        public int deleted { get; set; }
        public DateTime created_at { get; set; }
        public int created_by { get; set; }

        public string[] pages { get; set; }
        public string pdf_url { get; set; }
        public string txt_url { get; set; }
        public string docx_url { get; set; }
    }

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
        private readonly IJobStorage _jobStorage;
        private readonly IWebHostEnvironment _environment;
        private readonly PythonHelper _pythonHelper;
        private readonly DocumentHelper _documentHelper;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly WorkerOptions _options;
        private readonly BillingOptions _billingOptions;

        public OcrJob(
            IDbContext dbContext,
            IStorage storage,
            IJobStorage jobStorage,
            IConfiguration configuration,
            IWebHostEnvironment _environment,
            PythonHelper pythonHelper,
            DocumentHelper pdfHelper,
            IHttpClientFactory httpClientFactory)
        {
            _dbContext = dbContext;
            _storage = storage;
            _jobStorage = jobStorage;
            this._environment = _environment;
            _pythonHelper = pythonHelper;
            _documentHelper = pdfHelper;
            _httpClientFactory = httpClientFactory;
            _options = configuration.GetSection(Constants.Worker).Get<WorkerOptions>();
            _billingOptions = configuration.GetSection(Constants.Billing).Get<BillingOptions>();
        }

        [JobDisplayName("OCR Job ({0})")]
        [AutomaticRetry(Attempts = 0)]
        public async Task ExecuteOcrJob(string jobId, PerformContext context)
        {
            try
            {
                context.WriteLine($"Processing {jobId}");

                await _jobStorage.BeginTransactionAsync(context.CancellationToken.ShutdownToken);

                // Step 1: Make sure the job is not processed
                var job = await _jobStorage.GetJobAsync(jobId);
                var user = await _jobStorage.GetUserAsync(job.UserId);

                if (!await EnsureEnoughBalance(user, job, context))
                {
                    await _jobStorage.CommitTransactionAsync(context.CancellationToken.ShutdownToken);
                    return;
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                job.Status = ModelConstants.Processing;
                job.ProcessedAt = DateTime.UtcNow;
                await _jobStorage.UpdateJobAsync(job);

                context.CancellationToken.ThrowIfCancellationRequested();

                // Step 2: Download images
                var originalPrefix = $"{Constants.Original}/{job.UserId}/{job.Id}";
                var originalPath = Path.Combine(Path.GetTempPath(), Constants.Cranelift);

                await _storage.DownloadBlobs(originalPrefix, originalPath, context.CancellationToken.ShutdownToken);

                context.CancellationToken.ThrowIfCancellationRequested();

                // TODO: Make sure user has enough balance!

                var pages = Directory.EnumerateFiles(Path.Combine(originalPath, originalPrefix))
                                     .OrderBy(p => GetIndex(p))
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

                await _jobStorage.DeletePreviousPagesAsync(job.Id);

                // Process the pages
                foreach (var chunk in chunks)
                {
                    var tasks = chunk.Select(p => ProcessPage(job, p, context.CancellationToken.ShutdownToken)).ToArray();
                    await Task.WhenAll(tasks);

                    foreach (var task in tasks.Where(t => t.Result.Succeeded == false))
                    {
                        throw new InvalidOperationException($"Failed to process page: {task.Result.Name}. Output:\n{task.Result.Result}");
                    }

                    if (tasks.Any(t => t.Result.Succeeded == false))
                    {
                        job.Status = ModelConstants.Failed;
                        job.FailingReason = "Failed to process one or more pages.";
                        break;
                    }
                    else
                    {
                        const int minimumNumberOfWordsPerPage = 50;
                        foreach (var page in chunk)
                        {
                            page.IsFree = page.Result.CountWords() < minimumNumberOfWordsPerPage;
                            page.Processed = true;
                            await _jobStorage.InsertPageAsync(page);
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

                    job.PaidPageCount = pages.Count(p => p.IsFree == false);

                    context.WriteLine("Charging for the job...");
                    await _jobStorage.InsertUserTransactionAsync(new UserTransaction
                    {
                        UserId = job.UserId,
                        PageCount = -job.PaidPageCount,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = job.UserId,
                        PaymentMediumId = UserTransaction.PaymentMediums.ZhirBalance,
                        TypeId = UserTransaction.Types.OcrJob,
                        Confirmed = true,
                    });
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                if (!await EnsureEnoughBalance(user, job, context))
                {
                    return;
                }
                else
                {
                    // Update job :)
                    job.FinishedAt = DateTime.UtcNow;
                    await _jobStorage.UpdateJobAsync(job);
                }

                await _jobStorage.CommitTransactionAsync(context.CancellationToken.ShutdownToken);

                // Clean up temp folder
                Directory.Delete(Path.Combine(originalPath, Constants.Original, job.UserId.ToString(), job.Id), recursive: true);
                Directory.Delete(Path.Combine(Path.GetTempPath(), Constants.Cranelift, Constants.Done, job.UserId.ToString(), job.Id), recursive: true);

                try
                {
                    if (job.FromAPI == true && string.IsNullOrEmpty(job.Callback) == false)
                    {
                        context.WriteLine("Calling webhook...");
                        await CallWebhook(job, pages);
                    }
                }
                catch (Exception ex)
                {
                    context.WriteLine($"Error calling webhook: {ex}");
                }

                context.WriteLine("Done :)");
            }
            catch (Exception ex)
            {
                _jobStorage.RollbackTransaction();

                var job = await _jobStorage.GetJobAsync(jobId);

                job.Status = ModelConstants.Failed;
                job.FailingReason = ex.Message;
                job.FinishedAt = DateTime.UtcNow;
                await _jobStorage.UpdateJobAsync(job);

                throw;
            }
        }

        private string GetAssetUrl(int userId, string jobId, string name)
        {
            return $"https://zhir.io/assets/done/{userId}/{jobId}/{name}";
        }

        private async Task CallWebhook(Job job, Page[] pages)
        {
            var dto = new WebhookDto
            {
                id = job.Id,
                code = job.Code,
                created_at = job.CreatedAt,
                created_by = job.CreatedBy,
                deleted = job.Deleted == true ? 1 : 0,
                finished_at = job.FinishedAt,
                lang = job.Lang,
                name = job.Name,
                page_count = job.PageCount,
                paid_page_count = job.PaidPageCount,
                processed_at = job.ProcessedAt,
                queued_at = job.QueuedAt,
                user_failing_reason = job.UserFailingReason,
                status = job.Status,
                user_id = job.UserId,
                docx_url = job.Status == ModelConstants.Completed ? GetAssetUrl(job.UserId, job.Id, "result.docx") : null,
                pdf_url = job.Status == ModelConstants.Completed ? GetAssetUrl(job.UserId, job.Id, "result.pdf") : null,
                txt_url = job.Status == ModelConstants.Completed ? GetAssetUrl(job.UserId, job.Id, "result.txt") : null,
                pages = job.Status == ModelConstants.Completed ? pages.Select(p => p.Result).ToArray() : new string[0],
            };

            var uri = new Uri(job.Callback);

            if (uri.Host.ToLowerInvariant() == "localhost")
                return;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);

            var content = new StringContent(JsonConvert.SerializeObject(dto), Encoding.UTF8, "application/json");
            await client.PostAsync(uri.AbsoluteUri, content);
        }

        private async Task<bool> EnsureEnoughBalance(User user, Job job, PerformContext context)
        {
            if (_billingOptions.EnforceBalance && (user.Balance < job.PaidPageCount))
            {
                context.WriteLine($"Not enough balance. Needed balance: {job.PaidPageCount}. User Balance: {user.Balance}");
                job.FailingReason = "Not enough balance.";
                job.UserFailingReason = "باڵانسی پێویستت نییە.";
                job.Status = ModelConstants.Failed;
                job.FinishedAt = DateTime.UtcNow;
                await _jobStorage.UpdateJobAsync(job);

                return false;
            }

            return true;
        }

        private int GetIndex(string p)
        {
            var fileName = Path.GetFileNameWithoutExtension(p);
            if (int.TryParse(fileName, out var index))
            {
                return index;
            }

            throw new InvalidOperationException($"Invalid filename: '{p}'");
        }

        private async Task<Page> ProcessPage(Job job, Page page, System.Threading.CancellationToken cancellationToken)
        {
            var doneKey = $"{Constants.Done}/{job.UserId}/{job.Id}/{page.Name}";
            var donePath = Path.Combine(Path.GetTempPath(), Constants.Cranelift, Constants.Done, job.UserId.ToString(), job.Id, page.Name);

            var cleanResult = await Clean(page.FullPath, donePath, cancellationToken);
            page.Succeeded = cleanResult.Successful;
            page.PredictSizes = false; // !cleanResult.Cleaned;

            if (page.Succeeded)
            {
                var result = await RunTesseract(donePath, cancellationToken, job.GetLanguages());
                page.Succeeded = result.Success;

                if (page.Succeeded)
                {
                    page.Result = result.TextOutput;
                    page.HocrResult = result.HocrOutput;
                    page.PdfResult = result.PdfOutput;
                    // page.FormatedResult
                    page.Succeeded = await _storage.UploadBlob(doneKey, donePath, cancellationToken: cancellationToken);
                }
                else
                {
                    page.Result = result.TextOutput;
                }
            }
            else
            {
                page.Result = cleanResult.Output;
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
            var validExtensions = new HashSet<string>
            {
                ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".bmp", ".tiff"
            };

            return validExtensions.Contains(extension);
        }

        private class TesseractResult
        {
            public bool Success { get; set; }
            public string TextOutput { get; set; }
            public string HocrOutput { get; set; }
            public byte[] PdfOutput { get; set; }
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
                    languages = new[] { "ckb" };
                }
                else if (languages.Contains("ara") && languages.Contains("ckb"))
                {
                    languages = languages.Where(l => l != "ara").ToArray();
                }

                var workingDir = Path.Combine(_environment.ContentRootPath, "Dependencies/zhirpy");
                var scriptPath = Path.Combine(workingDir, "src/tess.py");

                var langs = string.Join("+", languages);

                var command = Command.Run("tesseract", new[] { $"-l {langs} {imageFile} {Path.Combine(tempOutputDir, "result")} txt hocr pdf" }, options =>
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
                    var pdf = await File.ReadAllBytesAsync(Path.Combine(tempOutputDir, "result.pdf"));

                    return new TesseractResult
                    {
                        TextOutput = txt,
                        HocrOutput = hocr,
                        PdfOutput = pdf,
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
