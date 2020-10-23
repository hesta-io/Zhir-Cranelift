using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Cranelift.Helpers
{
    public interface IDbContext
    {
        Task<DbConnection> OpenConnectionAsync(string connectionStringName, CancellationToken token);
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
                 CancellationToken token)
        {
            var connectionString = _configuration.GetConnectionString(connectionStringName);

            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(token);
            return connection;
        }
    }
}
