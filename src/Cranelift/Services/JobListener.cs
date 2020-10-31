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
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE job SET status = '{ModelConstants.Queued}', queued_at = @currentTime WHERE id in ({ids})";
            command.AddParameterWithValue("currentTime", DateTime.UtcNow);

            await command.ExecuteNonQueryAsync();
        }

        private static ListenerOptions GetOptions(IServiceProvider serviceProvider)
        {
            var configs = serviceProvider.GetService<IConfiguration>();
            var options = configs.GetSection(Constants.Listener).Get<ListenerOptions>();
            return options;
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

                    using (var connection = await context.OpenConnectionAsync(Constants.OcrConnectionName, cancellationToken))
                    {
                        var pendingJobs = await connection.GetPendingJobsAsync();

                        if (pendingJobs.Any())
                        {
                            // var api = JobStorage.Current.GetMonitoringApi();

                            foreach (var job in pendingJobs)
                            {
                                var jobId = scheduler.Enqueue<ProcessStep>(s => s.Execute(job.Id, null));
                                // TODO: What if we store the hangfire job id in the databse row?
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
