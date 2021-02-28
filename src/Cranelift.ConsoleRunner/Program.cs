using Cranelift.Common;
using Cranelift.Common.Abstractions;
using Cranelift.Common.Helpers;
using Cranelift.Common.Models;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.ConsoleRunner
{
    class FileBlobStorage : IBlobStorage
    {
        private readonly string _inputDir;
        private readonly string _outputDir;

        public FileBlobStorage(string inputDir, string outputDir)
        {
            _inputDir = inputDir;
            _outputDir = outputDir;
        }

        public async Task DownloadBlobs(int userId, string jobId, Func<string, Stream> getDestinationStream, CancellationToken cancellationToken)
        {
            var files = Directory.EnumerateFiles(_inputDir);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputStream = getDestinationStream(Path.GetFileName(file));
                using (var inputStream = File.OpenRead(file))
                    await inputStream.CopyToAsync(outputStream);
            }
        }

        public async Task<bool> UploadBlob(int userId, string jobId, string name, Stream data, string contentType, CancellationToken cancellationToken)
        {
            try
            {
                var fullPath = Path.Combine(_outputDir, name);
                using (var file = File.OpenWrite(fullPath))
                {
                    await data.CopyToAsync(file);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: cranelift inputDir outputDir [langs]");
                return;
            }

            var inputDir = args[0];
            var outputDir = args[1];
            var langs = "ckb";
            if (args.Length >= 3)
                langs = args[2];

            var job = new Job
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = 1,
                Lang = langs,
                Status = ModelConstants.Queued,
            };

            var pythonOptions = new PythonOptions
            {
                Path = "python3"
            };

            var files = Directory.EnumerateFiles(inputDir);
            var tempFolder = Path.Combine(Path.GetTempPath(), "cranelift", "original", job.UserId.ToString(), job.Id);
            Directory.CreateDirectory(tempFolder);
            int i = 0;
            foreach (var file in files)
            {
                var destinationPath = Path.Combine(tempFolder, i + Path.GetExtension(file));
                File.Copy(file, destinationPath);

                i++;
            }

            var pipeline = new OcrPipeline(
                line => Console.WriteLine(line),
                new FileBlobStorage(inputDir, outputDir),
                new DocumentHelper(),
                new PythonHelper(pythonOptions),
                new OcrPipelineOptions
                {
                    WorkerCount = 1,
                    ParallelPagesCount = 1,
                    TesseractModelsDirectory = Path.GetFullPath("dependencies/models"),
                    ZhirPyDirectory = Path.GetFullPath("dependencies/zhirpy"),
                });

            var result = await pipeline.RunAsync(job, default);

            try
            {
                Directory.Delete(tempFolder, recursive: true);
            }
            catch (Exception) { }

            Console.WriteLine($"Result: {result.Status}");
        }
    }
}
