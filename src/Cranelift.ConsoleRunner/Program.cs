using Cranelift.Common;
using Cranelift.Common.Abstractions;
using Cranelift.Common.Helpers;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.ConsoleRunner
{
    class FileBlobStorage : IBlobStorage
    {
        public FileBlobStorage(string basePath)
        {

        }

        public Task DownloadBlobs(string prefix, Func<string, Stream> getDestinationStream, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UploadBlob(string key, Stream data, string contentType, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var pythonOptions = new PythonOptions
            {
                Path = "python"
            };

            var pipeline = new OcrPipeline(
                line => Console.WriteLine(line),
                new FileBlobStorage(""),
                new DocumentHelper(),
                new PythonHelper(pythonOptions),
                new OcrPipelineOptions
                {
                    WorkerCount = 1,
                    ParallelPagesCount = 1,
                    TesseractModelsDirectory = "",
                    ZhirPyDirectory = "",
                });
        }
    }
}
