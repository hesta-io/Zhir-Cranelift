using Amazon.S3;
using Amazon.S3.Model;

using Cranelift.Common;
using Cranelift.Common.Abstractions;

using Microsoft.Extensions.Configuration;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Helpers
{
    public class S3Storage : IBlobStorage
    {
        private StorageOptions _options;
        private AmazonS3Client _client;

        public S3Storage(IConfiguration configuration)
        {
            _options = configuration.GetSection(Constants.Storage).Get<StorageOptions>();
            _client = new AmazonS3Client(_options.AccessKey, _options.Secret, new AmazonS3Config
            {
                ServiceURL = _options.HostName
            });
        }

        private async Task DownloadBlobs(
            string prefix,
            Func<string, Stream> getDestinationStream,
            CancellationToken cancellationToken)
        {
            // NOTE: This method only lists top 1000 objects!

            var query = new ListObjectsV2Request
            {
                Prefix = prefix,
                BucketName = _options.BucketName,
            };

            var response = await _client.ListObjectsV2Async(query, cancellationToken);

            foreach (var blob in response.S3Objects.Where(o => o.Key.EndsWith("/") == false))
            {
                var blobResponse = await _client.GetObjectAsync(blob.BucketName, blob.Key);
                using var destinationStream = getDestinationStream(blob.Key);

                await blobResponse.ResponseStream.CopyToAsync(destinationStream);
            }
        }

        private async Task<bool> UploadBlob(string key, Stream data, string contentType, CancellationToken cancellationToken)
        {
            var request = new PutObjectRequest
            {
                ContentType = contentType,
                InputStream = data,
                Key = key,
                BucketName = _options.BucketName,
            };

            var response = await _client.PutObjectAsync(request, cancellationToken);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }

        private async Task DownloadBlobs(string prefix, string directory, CancellationToken cancellationToken)
        {
            await DownloadBlobs(prefix, key =>
            {
                var path = Path.Combine(directory, key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                return File.OpenWrite(path);
            }, cancellationToken);
        }

        private async Task<bool> UploadBlob(
            string key,
            string filePath,
            string contentType = null,
            CancellationToken cancellationToken = default)
        {
            if (contentType is null)
            {
                if (filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = "image/jpeg";
                }
                else if (filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = "image/png";
                }
            }

            using (var stream = File.OpenRead(filePath))
            {
                return await UploadBlob(key, stream, contentType, cancellationToken);
            }
        }

        public async Task DownloadBlobs(int userId, string jobId, Func<string, Stream> getDestinationStream, CancellationToken cancellationToken)
        {
            var originalPrefix = $"{Constants.Original}/{userId}/{jobId}";
            await DownloadBlobs(originalPrefix, getDestinationStream, cancellationToken);
        }

        public async Task<bool> UploadBlob(int userId, string jobId, string name, Stream data, string contentType, CancellationToken cancellationToken)
        {
            var key = $"{Constants.Done}/{userId}/{jobId}/{name}";
            return await UploadBlob(key, data, contentType, cancellationToken);
        }
    }
}
