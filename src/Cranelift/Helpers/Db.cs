﻿using Dapper;

using Microsoft.Extensions.Configuration;

using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Cranelift.Common.Models;
using Cranelift.Common;

namespace Cranelift.Helpers
{
    public interface IDbContext
    {
        Task<DbConnection> OpenConnectionAsync(string connectionStringName, CancellationToken token = default);
    }

    public static class Extensions
    {
        public static Task<DbConnection> OpenOcrConnectionAsync(this IDbContext dbContext, CancellationToken token = default)
        {
            return dbContext.OpenConnectionAsync(Constants.OcrConnectionName, token);
        }
    }

    public static class Queries
    {
        public static async Task<IEnumerable<Job>> GetPendingJobsAsync(this DbConnection connection)
        {
            var sql = $"SELECT * FROM job WHERE STATUS = '{ModelConstants.Pending}'";
            return await connection.QueryAsync<Job>(sql);
        }

        public class TranactionViewModel
        {
            public int Id { get; set; }
            public string TransactionId { get; set; }
            public decimal? Amount { get; set; }
            public int PageCount { get; set; }
            public string PaymentMedium { get; set; }
            public string PaymentMediumCode { get; set; }
            public string UserNote { get; set; }
            public string AdminNote { get; set; }
            public bool? Confirmed { get; set; }
            public string Type { get; set; }
            public DateTime Date { get; set; }
        }

        public static async Task UpdateUserAsync(this DbConnection connection, User user)
        {
            var sql = $"UPDATE `user` SET verified = {(user.Verified == true ? 1 : 0)}, can_use_api = {(user.CanUseAPI == true ? 1 : 0)}, monthly_recharge = {user.MonthlyRecharge ?? 0}, api_key = @apiKey WHERE id = '{user.Id}'";
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            command.AddParameterWithValue("apiKey", user.APIKey);

            await command.ExecuteNonQueryAsync();
        }

        public static async Task<IEnumerable<TranactionViewModel>> GetTransactionsAsync(this DbConnection connection, int? userId = null)
        {
            var condition = userId is null ? "" : $"where ut.user_id = {userId}";

            var sql = $@"select ut.id, ut.amount, ut.confirmed, pm.name as PaymentMedium, pm.code as PaymentMediumCode, tt.name as Type, ut.transaction_id, ut.created_at as Date, ut.user_note, ut.admin_note, ut.page_count from user_transaction ut
left join payment_medium pm on pm.id = ut.payment_medium_id 
left join transaction_type tt on tt.id = ut.type_id
{condition}
order by ut.created_at desc";

            return await connection.QueryAsync<TranactionViewModel>(sql);
        }

        private const string UserQuery = @"select id, name, company_name, email, phone_no, deleted, created_at, verified, is_admin, can_use_api, api_key, monthly_recharge,
		(select sum(page_count) from user_transaction ut where ut.user_id = u.id and ut.confirmed = 1) as balance,
		(select sum(amount) from user_transaction ut where ut.user_id = u.id and ut.confirmed = 1) as money_spent,
		(select sum(page_count) from job j2 where j2.user_id = u.id) as count_pages,
		(select count(id) from job j2 where j2.user_id = u.id) as count_jobs
from `user` u";

        public static async Task<IEnumerable<User>> GetUsersAsync(this DbConnection connection)
        {
            return await connection.QueryAsync<User>(UserQuery);
        }

        public static async Task<User> GetUserAsync(this DbConnection connection, int id)
        {
            var sql = UserQuery + $" where u.id = {id}";
            return await connection.QueryFirstOrDefaultAsync<User>(sql);
        }

        public static async Task<Job> GetJobAsync(this DbConnection connection, string id)
        {
            var sql = $"SELECT * FROM job WHERE id = '{id}'";
            return await connection.QueryFirstOrDefaultAsync<Job>(sql);
        }

        public static async Task DeletePreviousPagesAsync(this DbConnection connection, Job job)
        {
            var sql = $"UPDATE page SET deleted = 1 WHERE job_id = '{job.Id}'";
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            await command.ExecuteNonQueryAsync();
        }

        public static async Task InsertPageAsync(this DbConnection connection, Page page)
        {
            var sql = $@"INSERT INTO page
(id, name, user_id, job_id, started_processing_at, processed, finished_processing_at, succeeded, `result`, deleted, created_at, created_by, is_free)
VALUES(@id, @name, @userId, @jobId , @startedAt, @processed, @finishedAt, @succeeded, @result, @deleted, @createdAt, @createdBy, @isFree);
";

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            command.AddParameterWithValue("id", page.Id);
            command.AddParameterWithValue("name", page.Name);
            command.AddParameterWithValue("userId", page.UserId);
            command.AddParameterWithValue("jobId", page.JobId);
            command.AddParameterWithValue("result", page.Result);
            command.AddParameterWithValue("processed", page.Processed);
            command.AddParameterWithValue("succeeded", page.Succeeded);
            command.AddParameterWithValue("startedAt", page.StartedProcessingAt);
            command.AddParameterWithValue("finishedAt", page.FinishedProcessingAt);
            command.AddParameterWithValue("deleted", page.Deleted);
            command.AddParameterWithValue("createdAt", page.CreatedAt);
            command.AddParameterWithValue("createdBy", page.CreatedBy);
            command.AddParameterWithValue("isFree", page.IsFree);

            await command.ExecuteNonQueryAsync();
        }
        
        public static async Task InsertTransactionAsync(this DbConnection connection, UserTransaction transaction)
        {
            transaction.UserNote = transaction.UserNote ?? "";
            transaction.AdminNote = transaction.AdminNote ?? "";

            if (string.IsNullOrWhiteSpace(transaction.TransactionId) == false)
            {
                transaction.TransactionId = transaction.TransactionId.Trim();
                var sql = $"SELECT COUNT(*) from user_transaction WHERE transaction_id = '{transaction.TransactionId}' AND payment_medium_id={transaction.PaymentMediumId} AND type_id={transaction.TypeId}";
                var count = await connection.ExecuteScalarAsync<int>(sql);
                if (count > 0) throw new InvalidOperationException("This Transaction has already been recharged!");
            }

            using var command = connection.CreateCommand();
            command.CommandText = $@"INSERT INTO user_transaction
(user_id, type_id, payment_medium_id, amount, page_count, user_note, admin_note, transaction_id, created_at, created_by, confirmed)
VALUES('{transaction.UserId}', '{transaction.TypeId}', '{transaction.PaymentMediumId}', '{transaction.Amount ?? 0}', '{transaction.PageCount}', @userNote, @adminNote, @transactionId, @createdAt, '{transaction.CreatedBy}', {(transaction.Confirmed == true ? 1 : 0)});
";

            command.AddParameterWithValue("transactionId", transaction.TransactionId);
            command.AddParameterWithValue("createdAt", transaction.CreatedAt);
            command.AddParameterWithValue("userNote", transaction.UserNote);
            command.AddParameterWithValue("adminNote", transaction.AdminNote);

            await command.ExecuteNonQueryAsync();
        }

        public static async Task UpdateJobAsync(this DbConnection connection, Job job)
        {
            var command = connection.CreateCommand();
            command.CommandText = $@"UPDATE job
SET name=@name, code='{job.Code}', user_id={job.UserId}, page_count={job.PageCount}, status='{job.Status}',
paid_page_count={job.PaidPageCount}, queued_at=@queuedAt, processed_at=@processedAt, finished_at=@finishedAt,
failing_reason=@failingReason, user_failing_reason=@userFailingReason, deleted=@deleted
WHERE id='{job.Id}'";

            command.AddParameterWithValue("name", job.Name);
            command.AddParameterWithValue("queuedAt", job.QueuedAt);
            command.AddParameterWithValue("processedAt", job.ProcessedAt);
            command.AddParameterWithValue("finishedAt", job.FinishedAt);

            var failingReason = job.FailingReason;
            if (failingReason.Length > 450)
                failingReason = failingReason.Substring(0, 450);

            command.AddParameterWithValue("failingReason", failingReason);
            command.AddParameterWithValue("userFailingReason", job.UserFailingReason);
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
