using Cranelift.Helpers;
using Cranelift.Steps;

using Dapper;

using Hangfire;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using MySql.Data.MySqlClient;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Services
{
    public class ListenerOptions
    {
        public double IntervalSeconds { get; set; }
    }

    public class JobListener : BackgroundService
    {
        public JobListener(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        private async Task UpdateJobStatusAsync(DbConnection connection, IEnumerable<string> jobIds)
        {
            var ids = string.Join(",", jobIds.Select(id => $"'{id}'"));
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"UPDATE job SET status = '{ModelConstants.Queued}' WHERE id in ({ids})";
                await command.ExecuteNonQueryAsync();
            }
        }

        private static ListenerOptions GetOptions(IServiceProvider serviceProvider)
        {
            var configs = serviceProvider.GetService<IConfiguration>();
            var options = new ListenerOptions();
            configs.GetSection("Listener").Bind(options);
            return options;
        }

        private static async Task<IEnumerable<Job>> GetPendingJobsAsync(DbConnection connection)
        {
            var sql = $"SELECT * FROM job WHERE STATUS = '{ModelConstants.Pending}'";
            return await connection.QueryAsync<Job>(sql);
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double waitSeconds = 1;
                using (var scope = ServiceProvider.CreateScope())
                {
                    ListenerOptions options = GetOptions(scope.ServiceProvider);
                    waitSeconds = options.IntervalSeconds;

                    var scheduler = scope.ServiceProvider.GetService<IBackgroundJobClient>();

                    var context = scope.ServiceProvider.GetService<IDbContext>();

                    using (var connection = await context.OpenConnectionAsync("OcrConnection", cancellationToken))
                    using (var transaction = await connection.BeginTransactionAsync(
                                    System.Data.IsolationLevel.ReadUncommitted,
                                    cancellationToken))
                    {
                        var pendingJobs = await GetPendingJobsAsync(connection);

                        if (pendingJobs.Any())
                        {
                            foreach (var job in pendingJobs)
                            {
                                scheduler.Enqueue<ProcessStep>(s => s.Execute(job.Id, null));
                            }

                            await UpdateJobStatusAsync(connection, pendingJobs.Select(j => j.Id));
                        }
                    }
                }

                await Task.Delay((int)(waitSeconds * 1000));
            }
        }
    }
}
