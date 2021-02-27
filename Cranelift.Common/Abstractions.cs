using Cranelift.Common.Models;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Common.Abstractions
{
    public interface IJobStorage : IDisposable
    {
        Task BeginTransactionAsync(CancellationToken cancellationToken);
        Task CommitTransactionAsync(CancellationToken cancellationToken);
        void RollbackTransaction();

        Task<Job> GetJobAsync(string jobId);
        Task<User> GetUserAsync(int userId);

        Task UpdateJobAsync(Job job);
        Task DeletePreviousPagesAsync(string jobId);
        Task InsertPageAsync(Page page);
        Task InsertUserTransactionAsync(UserTransaction userTransaction);
    }

    public class StorageOptions
    {
        public string HostName { get; set; }
        public string AccessKey { get; set; }
        public string Secret { get; set; }
        public string BucketName { get; set; }
    }

    public interface IBlobStorage
    {
        Task DownloadBlobs(string prefix, Func<string, Stream> getDestinationStream, CancellationToken cancellationToken);
        Task<bool> UploadBlob(string key, Stream data, string contentType, CancellationToken cancellationToken);
    }

}
