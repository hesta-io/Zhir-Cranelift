using Cranelift.Helpers;

using Dapper;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cranelift.Api
{
    public class Dataset
    {
        public bool Fill { get; set; }
        public string Label { get; set; }
        public string BackgroundColor { get; set; }
        public List<double> Data { get; set; }
    }

    public class Chart
    {
        public string[] Labels { get; set; }
        public IEnumerable<Dataset> Datasets { get; set; }
    }

    public class DayChartQuery
    {
        public string Status { get; set; }
        public DateTime CreatedDate { get; set; }
        public int Count { get; set; }
    }

    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ChartsController : Controller
    {
        private readonly IDbContext _dbContext;

        public ChartsController(IDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private const string Blue = "#36A2EB";

        [HttpGet("jobs")]
        public async Task<Chart> GetJobCount(int days = 7)
        {
            var sql = @$"select status, created_date, count(*) as count from (
SELECT status, cast(DATE_FORMAT(created_at, '%Y-%m-%d') as date) as created_date from job
) days
where status in ('completed', 'failed') and DATEDIFF(UTC_TIMESTAMP(), created_date) <= {days}
group by created_date, status
order by created_date desc";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var items = await connection.QueryAsync<DayChartQuery>(sql);
                items = items.OrderBy(i => i.CreatedDate).ToArray();

                var dates = items.Select(i => i.CreatedDate).Distinct().ToHashSet();

                var statuses = items.GroupBy(i => i.Status).OrderBy(i => i.Key).ToDictionary(i => i.Key, i => i.ToList());
                var datasets = statuses.Select(s => new Dataset
                {
                    Label = s.Key,
                    BackgroundColor = s.Key == "completed" ? "#4BC0C0" : "#FF6384",
                    Data = new List<double>()
                }).ToDictionary(d => d.Label);

                foreach (var date in dates)
                {
                    foreach (var status in statuses)
                    {
                        var dataset = datasets[status.Key];
                        var value = status.Value.FirstOrDefault(s => s.CreatedDate == date);
                        if (value != null)
                        {
                            dataset.Data.Add(value.Count);
                        }
                        else
                        {
                            dataset.Data.Add(0);
                        }
                    }
                }

                return new Chart
                {
                    Datasets = datasets.Select(i => i.Value),
                    Labels = dates.Select(d => d.ToString("MM/dd")).ToArray()
                };
            }
        }

        [HttpGet("daily-sign-ups")]
        public async Task<Chart> GetSignUpsCount(int days = 7)
        {
            var sql = @$"select created_date, count(*) as count from (
SELECT cast(DATE_FORMAT(created_at, '%Y-%m-%d') as date) as created_date from user
) days
where DATEDIFF(UTC_TIMESTAMP(), created_date) <= {days}
group by created_date
order by created_date desc";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var items = await connection.QueryAsync<DayChartQuery>(sql);
                items = items.OrderBy(i => i.CreatedDate).ToArray();

                var dataset = new Dataset
                {
                    Label = "Sign Ups",
                    Data = items.Select(i => (double)i.Count).ToList(),
                    BackgroundColor = Blue,
                };

                return new Chart
                {
                    Datasets = new List<Dataset> { dataset },
                    Labels = items.Select(i => i.CreatedDate.ToString("MM/dd")).ToArray()
                };
            }
        }

        [HttpGet("daily-active-users")]
        public async Task<Chart> GetActiveDailyActiveUsers(int days = 7)
        {
            var sql = $@"select created_date, count(*) as count from (

select user_id, created_date, count(*) as count from (
SELECT user_id, cast(DATE_FORMAT(created_at, '%Y-%m-%d') as date) as created_date from job
) days
where DATEDIFF(UTC_TIMESTAMP(), created_date) <= {days}
group by created_date, user_id 

) a
group by created_date
order by created_date desc";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var items = await connection.QueryAsync<DayChartQuery>(sql);
                items = items.OrderBy(i => i.CreatedDate).ToArray();

                var dataset = new Dataset
                {
                    Label = "Active Users",
                    Data = items.Select(i => (double)i.Count).ToList(),
                    BackgroundColor = Blue
                };

                return new Chart
                {
                    Datasets = new List<Dataset> { dataset },
                    Labels = items.Select(i => i.CreatedDate.ToString("MM/dd")).ToArray()
                };
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(int days = 7)
        {
            var activeUsersSql = @$"select count(*) from (
select id, name, (select job.created_at from job where job.user_id = user.id ORDER by created_at DESC limit 1) as last_job from user
) a
where datediff(UTC_TIMESTAMP(), last_job) < {days}";

            var averageProcessingTimeSql = @$"select (finished_at - processed_at) / page_count from job
where DATEDIFF(UTC_TIMESTAMP(), created_at) < {days} and status = 'completed'";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var activeUsers = await connection.ExecuteScalarAsync<int>(activeUsersSql);
                var allUsers = await connection.ExecuteScalarAsync<int>("select count(*) from user");
                var newUsers = await connection.ExecuteScalarAsync<int>($"select count(*) from user where datediff(UTC_TIMESTAMP(), created_at) < {days}");
                var verifiedUsers = await connection.ExecuteScalarAsync<int>($"select count(*) from user where verified = true");

                var jobs = await connection.QueryAsync<string>("select status from job");
                var completedJobs = jobs.Count(s => s == "completed");
                var failedJobs = jobs.Count(s => s == "failed");
                var totalJobs = jobs.Count();

                var averageProcessingTimePerPage = await connection.ExecuteScalarAsync<double>(averageProcessingTimeSql);

                return Ok(new
                {
                    activeUsers,
                    allUsers,
                    newUsers,
                    verifiedUsers,
                    completedJobs,
                    failedJobs,
                    totalJobs,
                    averageProcessingTimePerPage
                });
            }
        }

    }
}
