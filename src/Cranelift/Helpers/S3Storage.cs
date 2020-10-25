using Amazon.S3;
using Amazon.S3.Model;

using Microsoft.Extensions.Configuration;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Cranelift.Helpers
{
    public class StorageOptions
    {
        public string HostName { get; set; }
        public string AccessKey { get; set; }
        public string Secret { get; set; }
        public string BucketName { get; set; }
    }

    public static class StorageExtensions
    {
        public static async Task DownloadBlobs(this IStorage storage, string prefix, string directory)
        {
            await storage.DownloadBlobs(prefix, key =>
            {
                var path = Path.Combine(directory, key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                return File.OpenWrite(path);
            });
        }

        public static async Task<bool> UploadBlob(this IStorage storage, string key, string filePath, string contentType = null)
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

            using var stream = File.OpenRead(filePath);
            return await storage.UploadBlob(key, stream, contentType);
        }
    }

    public interface IStorage
    {
        Task DownloadBlobs(string prefix, Func<string, Stream> getDestinationStream);
        Task<bool> UploadBlob(string key, Stream data, string contentType);
    }

    public class S3Storage : IStorage
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

        public async Task DownloadBlobs(
            string prefix,
            Func<string, Stream> getDestinationStream)
        {
            // NOTE: This method only lists top 1000 objects!

            var query = new ListObjectsV2Request
            {
                Prefix = prefix,
                BucketName = _options.BucketName,
            };

            var response = await _client.ListObjectsV2Async(query);

            foreach (var blob in response.S3Objects.Where(o => o.Key.EndsWith("/") == false))
            {
                var blobResponse = await _client.GetObjectAsync(blob.BucketName, blob.Key);
                using var destinationStream = getDestinationStream(blob.Key);

                await blobResponse.ResponseStream.CopyToAsync(destinationStream);
            }
        }

        public async Task<bool> UploadBlob(string key, Stream data, string contentType)
        {
            var request = new PutObjectRequest
            {
                ContentType = contentType,
                InputStream = data,
                Key = key,
                BucketName = _options.BucketName,
            };

            var response = await _client.PutObjectAsync(request);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }
    }
}
