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
        public string BorderColor { get; set; }
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
        public int Count2 { get; set; }
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
        public async Task<Chart> GetDailyJobs(int days = 7)
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

                var months = false;

                if (days >= 60)
                {
                    items = items.GroupBy(i => new Tuple<int, string>(i.CreatedDate.Month, i.Status))
                                 .Select(g => new DayChartQuery { Count = g.Sum(i => i.Count), CreatedDate = new DateTime(g.First().CreatedDate.Year, g.Key.Item1, 1), Status = g.Key.Item2 })
                                 .OrderBy(i => i.CreatedDate).ToArray();

                    months = true;
                }

                var dates = items.Select(i => i.CreatedDate).Distinct().ToHashSet();

                var statuses = items.GroupBy(i => i.Status).OrderBy(i => i.Key).ToDictionary(i => i.Key, i => i.ToList());
                var datasets = statuses.Select(s => new Dataset
                {
                    Label = s.Key,
                    BackgroundColor = s.Key == "completed" ? "#4BC0C0" : "#FF6384",
                    BorderColor = s.Key == "completed" ? "#4BC0C0" : "#FF6384",
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
                    Labels = dates.Select(d => months ? d.ToString("MMM yyyy") : d.ToString("MM/dd")).ToArray()
                };
            }
        }


        [HttpGet("pages")]
        public async Task<Chart> GetDailyPages(int days = 7)
        {
            var allPagesCount = @$"select status, created_date, sum(page_count) as count, sum(paid_page_count) as count2 from (
SELECT status, page_count, paid_page_count, cast(DATE_FORMAT(created_at, '%Y-%m-%d') as date) as created_date from job
) days
where status in ('completed') and DATEDIFF(UTC_TIMESTAMP(), created_date) <= {days}
group by created_date
order by created_date desc";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var items = await connection.QueryAsync<DayChartQuery>(allPagesCount);
                items = items.OrderBy(i => i.CreatedDate).ToArray();

                var months = false;

                if (days >= 60)
                {
                    items = items.GroupBy(i => i.CreatedDate.Month)
                                 .Select(g => new DayChartQuery { Count = g.Sum(i => i.Count), CreatedDate = new DateTime(g.First().CreatedDate.Year, g.Key, 1), Count2 = g.Sum(i => i.Count2) })
                                 .OrderBy(i => i.CreatedDate).ToArray();

                    months = true;
                }

                var paidPagesDataset = new Dataset
                {
                    Label = "Paid",
                    BackgroundColor = "#4BC0C0",
                    BorderColor = "#4BC0C0",
                    Data = new List<double>()
                };

                var freePagesDataset = new Dataset
                {
                    Label = "Free",
                    BackgroundColor = "#FF6384",
                    BorderColor = "#FF6384",
                    Data = new List<double>()
                };

                foreach (var item in items)
                {
                    paidPagesDataset.Data.Add(item.Count2);
                    freePagesDataset.Data.Add(item.Count - item.Count2);
                }

                return new Chart
                {
                    Datasets = new List<Dataset> { paidPagesDataset, freePagesDataset },
                    Labels = items.Select(d => months ? d.CreatedDate.ToString("MMM yyyy") : d.CreatedDate.ToString("MM/dd")).ToArray()
                };
            }
        }

        [HttpGet("users")]
        public async Task<Chart> GetDailyUsers(int days = 7)
        {
            var signUpsSql = @$"select created_date, count(*) as count from (
SELECT cast(DATE_FORMAT(created_at, '%Y-%m-%d') as date) as created_date from user
) days
where DATEDIFF(UTC_TIMESTAMP(), created_date) <= {days}
group by created_date
order by created_date desc";

            var activeUsersSql = $@"select created_date, count(*) as count from (

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
                var signUps = await connection.QueryAsync<DayChartQuery>(signUpsSql);
                var activeUsers = await connection.QueryAsync<DayChartQuery>(activeUsersSql);

                var months = false;

                if (days < 60)
                {
                    signUps = signUps.OrderBy(i => i.CreatedDate).ToArray();
                    activeUsers = activeUsers.OrderBy(i => i.CreatedDate).ToArray();
                }
                else
                {
                    signUps = signUps.GroupBy(i => i.CreatedDate.Month)
                        .Select(g => new DayChartQuery { Count = g.Sum(i => i.Count), CreatedDate = new DateTime(g.First().CreatedDate.Year, g.Key, 1) })
                        .OrderBy(i => i.CreatedDate).ToArray();

                    activeUsers = activeUsers.GroupBy(i => i.CreatedDate.Month)
                       .Select(g => new DayChartQuery { Count = g.Sum(i => i.Count), CreatedDate = new DateTime(g.First().CreatedDate.Year, g.Key, 1) })
                       .OrderBy(i => i.CreatedDate).ToArray();

                    months = true;
                }

                var signUpsDataset = new Dataset
                {
                    Label = "Sign Ups",
                    Data = new List<double>(),
                    BackgroundColor = Blue,
                    BorderColor = Blue,
                };

                var activeUsersDataset = new Dataset
                {
                    Label = "Active Users",
                    Data = new List<double>(),
                    BackgroundColor = "#800080",
                    BorderColor = "#800080",
                };

                var allDates = signUps.Select(s => s.CreatedDate).Union(activeUsers.Select(u => u.CreatedDate)).OrderBy(i => i).ToHashSet();

                foreach (var date in allDates)
                {
                    var signUp = signUps.FirstOrDefault(s => s.CreatedDate == date);
                    if (signUp is null)
                        signUpsDataset.Data.Add(0);
                    else
                        signUpsDataset.Data.Add(signUp.Count);

                    var activeUser = activeUsers.FirstOrDefault(s => s.CreatedDate == date);
                    if (activeUser is null)
                        activeUsersDataset.Data.Add(0);
                    else
                        activeUsersDataset.Data.Add(activeUser.Count);
                }

                return new Chart
                {
                    Datasets = new List<Dataset> { signUpsDataset, activeUsersDataset },
                    Labels = allDates.Select(i => months ? i.ToString("MMM yyyy") : i.ToString("MM/dd")).ToArray()
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

                var jobs = await connection.QueryAsync<string>($"select status from job where datediff(UTC_TIMESTAMP(), created_at) < {days}");
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

        [HttpGet("cumulative-users")]
        public async Task<Chart> GetCumulativeUsers(int days = 7)
        {
            var signUpsSql = @$"with signUps as (
	select created_date, count(*) as count from (
		SELECT cast(DATE_FORMAT(created_at, '%Y-%m-%d') as date) as created_date from user
	) days
	group by created_date
)

select created_date, sum(count) over (order by created_date) as count
from signUps;";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var cumulativeUsers = await connection.QueryAsync<DayChartQuery>(signUpsSql);
                cumulativeUsers = cumulativeUsers.Where(i => (DateTime.UtcNow - i.CreatedDate).TotalDays < days)
                                 .OrderBy(i => i.CreatedDate).ToArray();

                var dataset = new Dataset
                {
                    Label = "User growth",
                    Data = cumulativeUsers.Select(s => (double)s.Count).ToList(),
                    BackgroundColor = Blue,
                    BorderColor = Blue,
                };

                return new Chart
                {
                    Datasets = new List<Dataset> { dataset },
                    Labels = cumulativeUsers.Select(i => i.CreatedDate.ToString("MM/dd")).ToArray()
                };
            }
        }

    }
}
