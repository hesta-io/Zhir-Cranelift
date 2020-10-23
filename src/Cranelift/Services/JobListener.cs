using Cranelift.Helpers;

using Dapper;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using MySql.Data.MySqlClient;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Services
{
    public class ListenerOptions
    {
        public double IntervalSeconds { get; set; }
    }

    public class JobListener : IHostedService
    {
        public JobListener(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double waitSeconds = 1;
                using (var scope = ServiceProvider.CreateScope())
                {
                    ListenerOptions options = GetOptions(scope.ServiceProvider);
                    waitSeconds = options.IntervalSeconds;

                    using (var connection = await OpenConnectionAsync(scope.ServiceProvider, "OcrConnection", cancellationToken))
                    using (var transaction = await connection.BeginTransactionAsync(
                                    System.Data.IsolationLevel.ReadUncommitted,
                                    cancellationToken))
                    {
                        var pendingJobs = await GetPendingJobsAsync(connection);

                    }
                }

                await Task.Delay((int)(waitSeconds * 1000));
            }
        }

        private async Task<DbConnection> OpenConnectionAsync(
            IServiceProvider serviceProvider,
            string connectionStringName,
            CancellationToken token)
        {
            var configs = serviceProvider.GetService<IConfiguration>();
            var connectionString = configs.GetConnectionString(connectionStringName);

            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(token);
            return connection;
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

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
