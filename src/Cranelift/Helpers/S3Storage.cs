using Amazon.S3;
using Amazon.S3.Model;

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

        public async Task DownloadBlobs(
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

        public async Task<bool> UploadBlob(string key, Stream data, string contentType, CancellationToken cancellationToken)
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
    }
}
