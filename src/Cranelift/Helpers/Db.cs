using Dapper;

using Microsoft.Extensions.Configuration;

using MySql.Data.MySqlClient;

using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Helpers
{
    public interface IDbContext
    {
        Task<DbConnection> OpenConnectionAsync(string connectionStringName, CancellationToken token = default);
    }

    public static class Queries
    {
        public static async Task<IEnumerable<Job>> GetPendingJobsAsync(this DbConnection connection)
        {
            var sql = $"SELECT * FROM job WHERE STATUS = '{ModelConstants.Pending}'";
            return await connection.QueryAsync<Job>(sql);
        }

        public static async Task<Job> GetJobAsync(this DbConnection connection, string id)
        {
            var sql = $"SELECT * FROM job WHERE id = '{id}'";
            return await connection.QueryFirstOrDefaultAsync<Job>(sql);
        }

        public static async Task InsertPage(this DbConnection connection, Page page)
        {
            var sql = $@"INSERT INTO page
(id, name, user_id, job_id, started_processing_at, processed, finished_processing_at, succeeded, `result`, formated_result, deleted, created_at, created_by)
VALUES(@id, @name, @userId, @jobId , @startedAt, @processed, @finishedAt, @succeeded, @result, @formatedResult, @deleted, @createdAt, @createdBy);
";

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            command.AddParameterWithValue("id", page.Id);
            command.AddParameterWithValue("name", page.Name);
            command.AddParameterWithValue("userId", page.UserId);
            command.AddParameterWithValue("jobId", page.JobId);
            command.AddParameterWithValue("result", page.Result);
            command.AddParameterWithValue("formatedResult", page.FormatedResult);
            command.AddParameterWithValue("processed", page.Processed);
            command.AddParameterWithValue("succeeded", page.Succeeded);
            command.AddParameterWithValue("startedAt", page.StartedProcessingAt);
            command.AddParameterWithValue("finishedAt", page.FinishedProcessingAt);
            command.AddParameterWithValue("deleted", page.Deleted);
            command.AddParameterWithValue("createdAt", page.CreatedAt);
            command.AddParameterWithValue("createdBy", page.CreatedBy);

            await command.ExecuteNonQueryAsync();
        }

        public static async Task UpdateJob(this DbConnection connection, Job job)
        {
            var command = connection.CreateCommand();
            command.CommandText = $@"UPDATE job
SET name=@name, code='{job.Code}', user_id={job.UserId}, page_count={job.PageCount}, status='{job.Status}',
price_per_page={job.PricePerPage}, queued_at=@queuedAt, processed_at=@processedAt, finished_at=@finishedAt,
failing_reason=@failingReason, deleted=@deleted
WHERE id='{job.Id}'";

            command.AddParameterWithValue("name", job.Name);
            command.AddParameterWithValue("queuedAt", job.QueudAt);
            command.AddParameterWithValue("processedAt", job.ProcessedAt);
            command.AddParameterWithValue("finishedAt", job.FinishedAt);
            command.AddParameterWithValue("failingReason", job.FailingReason);
            command.AddParameterWithValue("deleted", job.Deleted);

            await command.ExecuteNonQueryAsync();
        }
    }

    public class MySqlDbContext : IDbContext
    {
        private readonly IConfiguration _configuration;

        public MySqlDbContext(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<DbConnection> OpenConnectionAsync(
                 string connectionStringName,
                 CancellationToken token = default)
        {
            var connectionString = _configuration.GetConnectionString(connectionStringName);

            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(token);
            return connection;
        }
    }
}
