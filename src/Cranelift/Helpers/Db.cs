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
