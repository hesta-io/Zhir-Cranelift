using Cranelift.Common.Models;

using System;
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
}
