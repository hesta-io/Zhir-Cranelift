using Cranelift.Common;
using Cranelift.Common.Abstractions;
using Cranelift.Common.Helpers;
using Cranelift.Common.Models;
using Cranelift.Helpers;

using Hangfire;
using Hangfire.Console;
using Hangfire.Server;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

using Newtonsoft.Json;

using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

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
        private readonly IBlobStorage _storage;
        private readonly IWebHostEnvironment _environment;
        private readonly PythonHelper _pythonHelper;
        private readonly DocumentHelper _documentHelper;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly WorkerOptions _options;
        private readonly BillingOptions _billingOptions;

        public OcrJob(
            IDbContext dbContext,
            IBlobStorage storage,
            IConfiguration configuration,
            IWebHostEnvironment _environment,
            DocumentHelper pdfHelper,
            IHttpClientFactory httpClientFactory)
        {
            _dbContext = dbContext;
            _storage = storage;
            this._environment = _environment;

            var pythonOptions = configuration.GetSection(Constants.Python).Get<PythonOptions>();
            _pythonHelper = new PythonHelper(pythonOptions);
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

                using (var connection = await _dbContext.OpenOcrConnectionAsync())
                using (var transaction = await connection.BeginTransactionAsync(
                    context.CancellationToken.ShutdownToken))
                {
                    // Step 1: Make sure the job is not processed
                    var job = await connection.GetJobAsync(jobId);
                    var user = await connection.GetUserAsync(job.UserId);

                    if (!await EnsureEnoughBalance(user, job, connection, context))
                    {
                        await transaction.CommitAsync(context.CancellationToken.ShutdownToken);
                        return;
                    }

                    context.CancellationToken.ThrowIfCancellationRequested();

                    job.Status = ModelConstants.Processing;
                    job.ProcessedAt = DateTime.UtcNow;
                    await connection.UpdateJobAsync(job);

                    context.CancellationToken.ThrowIfCancellationRequested();

                    await connection.DeletePreviousPagesAsync(job);

                    context.CancellationToken.ThrowIfCancellationRequested();

                    var pipeline = new OcrPipeline(
                        l => context.WriteLine(l),
                        _storage,
                        _documentHelper,
                        _pythonHelper,
                        new OcrPipelineOptions
                        {
                            ParallelPagesCount = _options.ParallelPagesCount,
                            WorkerCount = _options.WorkerCount,
                            TesseractModelsDirectory = Path.Combine(_environment.ContentRootPath, "Dependencies", "models"),
                            ZhirPyDirectory = Path.Combine(_environment.ContentRootPath, "Dependencies/zhirpy"),
                        });

                    var result = await pipeline.RunAsync(job, context.CancellationToken.ShutdownToken);

                    context.CancellationToken.ThrowIfCancellationRequested();

                    if (result.Status == OcrPipelineStatus.Completed)
                    {
                        foreach (var page in result.Pages)
                        {
                            await connection.InsertPageAsync(page);
                        }

                        context.WriteLine($"Charging ({job.PaidPageCount} pages) for the job...");

                        await connection.InsertTransactionAsync(new UserTransaction
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

                    if (!await EnsureEnoughBalance(user, job, connection, context))
                    {
                        return;
                    }
                    else
                    {
                        // Update job :)
                        job.FinishedAt = DateTime.UtcNow;
                        await connection.UpdateJobAsync(job);
                    }

                    await transaction.CommitAsync(context.CancellationToken.ShutdownToken);

                    try
                    {
                        if (job.FromAPI == true && string.IsNullOrEmpty(job.Callback) == false)
                        {
                            context.WriteLine("Calling webhook...");
                            await CallWebhook(job, result.Pages);
                        }
                    }
                    catch (Exception ex)
                    {
                        context.WriteLine($"Error calling webhook: {ex}");
                    }

                    context.WriteLine("Done :)");
                }
            }
            catch (Exception ex)
            {
                using (var connection = await _dbContext.OpenOcrConnectionAsync())
                {
                    var job = await connection.GetJobAsync(jobId);

                    job.Status = ModelConstants.Failed;
                    job.FailingReason = ex.Message;
                    job.FinishedAt = DateTime.UtcNow;
                    await connection.UpdateJobAsync(job);
                }

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

        private async Task<bool> EnsureEnoughBalance(User user, Job job, DbConnection connection, PerformContext context)
        {
            if (_billingOptions.EnforceBalance && (user.Balance < job.PaidPageCount))
            {
                context.WriteLine($"Not enough balance. Needed balance: {job.PaidPageCount}. User Balance: {user.Balance}");
                job.FailingReason = "Not enough balance.";
                job.UserFailingReason = "باڵانسی پێویستت نییە.";
                job.Status = ModelConstants.Failed;
                job.FinishedAt = DateTime.UtcNow;
                await connection.UpdateJobAsync(job);

                return false;
            }

            return true;
        }
    }
}