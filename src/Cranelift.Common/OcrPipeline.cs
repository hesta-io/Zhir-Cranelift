using Cranelift.Common.Abstractions;
using Cranelift.Common.Helpers;
using Cranelift.Common.Models;

using Medallion.Shell;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Common
{
    public enum OcrPipelineStatus
    {
        Completed,
        Failed
    }

    public class OcrPipelineResult
    {
        public OcrPipelineStatus Status { get; set; }
        public Page[] Pages { get; internal set; }
        public HocrPage[] HocrPages { get; set; }
    }

    public class OcrPipelineOptions
    {
        public int WorkerCount { get; set; }
        public int ParallelPagesCount { get; set; }
        public string ZhirPyDirectory { get; set; }
        public string TesseractModelsDirectory { get; set; }
    }

    public class OcrPipeline
    {
        private readonly Action<string> _logger;
        private readonly IBlobStorage _blobStorage;
        private readonly DocumentHelper _documentHelper;
        private readonly PythonHelper _pythonHelper;
        private readonly OcrPipelineOptions _options;

        public OcrPipeline(
            Action<string> logger,
            IBlobStorage blobStorage,
            DocumentHelper documentHelper,
            PythonHelper pythonHelper,
            OcrPipelineOptions options)
        {
            _logger = logger;
            _blobStorage = blobStorage;
            _documentHelper = documentHelper;
            _pythonHelper = pythonHelper;
            _options = options;
        }

        public async Task<OcrPipelineResult> RunAsync(Job job, CancellationToken cancellationToken)
        {
            var status = OcrPipelineStatus.Completed;

            var originalPath = Path.Combine(Path.GetTempPath(), Constants.Cranelift);
            var originalPrefix = $"{Constants.Original}/{job.UserId}/{job.Id}";

            await _blobStorage.DownloadBlobs(job.UserId, job.Id, originalPath, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

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

            cancellationToken.ThrowIfCancellationRequested();

            // Process the pages
            foreach (var chunk in chunks)
            {
                var tasks = chunk.Select(p => ProcessPage(job, p, cancellationToken)).ToArray();
                await Task.WhenAll(tasks);

                foreach (var task in tasks.Where(t => t.Result.Succeeded == false))
                {
                    throw new InvalidOperationException($"Failed to process page: {task.Result.Name}. Output:\n{task.Result.Result}");
                }

                if (tasks.Any(t => t.Result.Succeeded == false))
                {
                    status = OcrPipelineStatus.Failed;
                    break;
                }
                else
                {
                    const int minimumNumberOfWordsPerPage = 50;
                    foreach (var page in chunk)
                    {
                        page.IsFree = page.Result.CountWords() < minimumNumberOfWordsPerPage;
                        page.Processed = true;
                    }

                    count += parallelizationDegree;
                    _logger($"Progress: {count}/{job.PageCount}");
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            cancellationToken.ThrowIfCancellationRequested();

            HocrPage[] hocrPages = null;

            if (status == OcrPipelineStatus.Completed)
            {
                _logger("Generating pdf file...");
                var pdfBytes = _documentHelper.MergePages(pages.Select(p => p.PdfResult).ToList());
                await _blobStorage.UploadBlob(
                    job.UserId,
                    job.Id,
                    "result.pdf",
                    new MemoryStream(pdfBytes),
                    Constants.Pdf,
                    cancellationToken);

                _logger("Generating text file...");
                var text = string.Join("\n\n\n", pages.Select(p => p.Result));
                var textBytes = Encoding.UTF8.GetBytes(text);
                await _blobStorage.UploadBlob(
                    job.UserId,
                    job.Id,
                    "result.txt",
                    new MemoryStream(textBytes),
                    Constants.PlainText,
                    cancellationToken);

                _logger("Generating hocr file...");
                var hocr = string.Join("\n\n\n", pages.Select(p => p.HocrResult));
                var hocrBytes = Encoding.UTF8.GetBytes(hocr);
                await _blobStorage.UploadBlob(
                    job.UserId,
                    job.Id,
                    "result.hocrlist",
                    new MemoryStream(hocrBytes),
                    Constants.PlainText,
                    cancellationToken);

                _logger("Generating docx file...");
                var xml = File.ReadAllText("assets/table.xml");
                var tables = ICDAR19TableParser.ParseDocument(xml);

                hocrPages = pages.Select(p => HocrParser.Parse(p.HocrResult, p.PredictSizes, tables)).ToArray();
                var wordDocument = _documentHelper.CreateWordDocument(hocrPages);

                await _blobStorage.UploadBlob(
                    job.UserId,
                    job.Id,
                   "result.docx",
                   wordDocument,
                   Constants.Docx,
                   cancellationToken);

                job.Status = ModelConstants.Completed;
                job.PaidPageCount = pages.Count(p => p.IsFree == false);
            }
            else
            {
                job.Status = ModelConstants.Failed;
                job.FailingReason = "Failed to process one or more pages.";
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Clean up temp folder
            Directory.Delete(Path.Combine(originalPath, Constants.Original, job.UserId.ToString(), job.Id), recursive: true);
            Directory.Delete(Path.Combine(Path.GetTempPath(), Constants.Cranelift, Constants.Done, job.UserId.ToString(), job.Id), recursive: true);

            return new OcrPipelineResult
            {
                Status = status,
                Pages = pages,
                HocrPages = hocrPages
            };
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
                    page.Succeeded = await _blobStorage.UploadBlob(
                        job.UserId, job.Id, page.Name, filePath: donePath, cancellationToken: cancellationToken);
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
            var workingDir = _options.ZhirPyDirectory;
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
                var modelsPath = _options.TesseractModelsDirectory;

                if (languages is null || languages.Length == 0)
                {
                    languages = new[] { "ckb" };
                }
                else if (languages.Contains("ara") && languages.Contains("ckb"))
                {
                    languages = languages.Where(l => l != "ara").ToArray();
                }

                var workingDir = _options.ZhirPyDirectory;
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
                    var txt = File.ReadAllText(Path.Combine(tempOutputDir, "result.txt"));
                    var hocr = File.ReadAllText(Path.Combine(tempOutputDir, "result.hocr"));
                    var pdf = File.ReadAllBytes(Path.Combine(tempOutputDir, "result.pdf"));

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

        private static bool IsImage(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var validExtensions = new HashSet<string>
            {
                ".jpg", ".jpeg", ".jfif", ".png", ".webp", ".bmp", ".tiff"
            };

            return validExtensions.Contains(extension);
        }

        private static int GetIndex(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (int.TryParse(fileName, out var index))
            {
                return index;
            }

            throw new InvalidOperationException($"Invalid filename: '{path}'");
        }
    }
}
