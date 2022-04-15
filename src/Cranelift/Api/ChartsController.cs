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

    public class HeatMapElement
    {
        public int DayofWeek { get; set; }
        public int Hour { get; set; }
        public int Value { get; set; }
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


        [HttpGet("activity-heatmap")]
        public async Task<List<List<int>>> GetActivityHeatMap(int days = 7)
        {
            var sql = @$"select dayofweek(created_at) as day_of_week, hour(created_at) as hour, count(*) as value from (
	select created_at from job where DATEDIFF(UTC_TIMESTAMP(), created_at) <= {days}
) d
group by day_of_week, hour
order by day_of_week, hour";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var items = await connection.QueryAsync<HeatMapElement>(sql);
                var list = new List<List<int>>();

                for (int i = 1; i <= 7; i++) // 1 = Sunday, 7 = Saturday
                {
                    var hours = items.Where(x => x.DayofWeek == i).ToDictionary(x => x.Hour, x => x.Value);
                    var values = new List<int>();
                    list.Add(values);

                    for (int h = 0; h < 24; h++)
                    {
                        hours.TryGetValue(h, out var value);
                        values.Add(value);
                    }
                }

                return list;
            }
        }

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

                var mode = GroupMode.Days;

                if (days >= 120)
                {
                    items = items.GroupBy(i => new Tuple<int, int, string>(i.CreatedDate.Year, i.CreatedDate.Month, i.Status))
                                 .Select(g => new DayChartQuery { Count = g.Sum(i => i.Count), CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, 1), Status = g.Key.Item3 })
                                 .OrderBy(i => i.CreatedDate).ToArray();

                    mode = GroupMode.Months;
                }
                else if (days > 90)
                {
                    items = GroupDays(items, 10).OrderBy(i => i.CreatedDate)
                       .ToArray();

                    mode = GroupMode.NDays;
                }
                else if (days > 30)
                {
                    items = GroupDays(items, 5).OrderBy(i => i.CreatedDate)
                       .ToArray();

                    mode = GroupMode.NDays;
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
                    Labels = dates.Select(d => Format(d, mode)).ToArray()
                };
            }

            IEnumerable<DayChartQuery> GroupDays(IEnumerable<DayChartQuery> items, int days)
            {
                return items
                    .GroupBy(i => 
                        new Tuple<int, int, int, string>(
                            i.CreatedDate.Year, 
                            i.CreatedDate.Month, 
                            RoundOff(i.CreatedDate.Day, days), i.Status)
                        )

                    .Select(g => 
                        new DayChartQuery { 
                            Count = g.Sum(i => i.Count), 
                            CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, g.Key.Item3), 
                            Status = g.Key.Item4 
                        });
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

                var mode = GroupMode.Days;

                if (days >= 120)
                {
                    items = items.GroupBy(i => new Tuple<int, int, string>(i.CreatedDate.Year, i.CreatedDate.Month, i.Status))
                                 .Select(g => new DayChartQuery { Count = g.Sum(i => i.Count), Count2 = g.Sum(i => i.Count2), CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, 1), Status = g.Key.Item3 })
                                 .OrderBy(i => i.CreatedDate).ToArray();

                    mode = GroupMode.Months;
                }
                else if (days > 90)
                {
                    items = GroupDays(items, 10).OrderBy(i => i.CreatedDate)
                       .ToArray();

                    mode = GroupMode.NDays;
                }
                else if (days > 30)
                {
                    items = GroupDays(items, 5).OrderBy(i => i.CreatedDate)
                       .ToArray();

                    mode = GroupMode.NDays;
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
                    Labels = items.Select(d => Format(d.CreatedDate, mode)).ToArray()
                };
            }

            IEnumerable<DayChartQuery> GroupDays(IEnumerable<DayChartQuery> items, int days)
            {
                return items
                    .GroupBy(i => 
                        new Tuple<int, int, int, string>(
                            i.CreatedDate.Year, 
                            i.CreatedDate.Month, 
                            RoundOff(i.CreatedDate.Day, days), 
                            i.Status)
                        )

                    .Select(g => 
                        new DayChartQuery
                        {
                            Count = g.Sum(i => i.Count),
                            Count2 = g.Sum(i => i.Count2),
                            CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, g.Key.Item3),
                            Status = g.Key.Item4
                        });
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

                var mode = GroupMode.Days;

                if (days >= 120)
                {
                    signUps = signUps.GroupBy(i => new Tuple<int, int>(i.CreatedDate.Year, i.CreatedDate.Month))
                       .Select(g => new DayChartQuery { Count = g.Sum(i => i.Count), CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, 1) })
                       .OrderBy(i => i.CreatedDate).ToArray();

                    activeUsers = activeUsers.GroupBy(i => new Tuple<int, int>(i.CreatedDate.Year, i.CreatedDate.Month))
                       .Select(g => new DayChartQuery { Count = g.Sum(i => i.Count), CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, 1) })
                       .OrderBy(i => i.CreatedDate).ToArray();

                    mode = GroupMode.Months;
                }
                else if (days > 90)
                {
                    signUps = GroupDaysSignUps(signUps, 10).OrderBy(i => i.CreatedDate).ToArray();

                    activeUsers = GroupDaysActiveUsers(activeUsers, 10).OrderBy(i => i.CreatedDate).ToArray();

                    mode = GroupMode.NDays;
                }
                else if (days > 30)
                {
                    signUps = GroupDaysSignUps(signUps, 5).OrderBy(i => i.CreatedDate).ToArray();

                    activeUsers = GroupDaysActiveUsers(activeUsers, 5).OrderBy(i => i.CreatedDate).ToArray();

                    mode = GroupMode.NDays;
                }
                else
                {
                    signUps = signUps.OrderBy(i => i.CreatedDate).ToArray();
                    activeUsers = activeUsers.OrderBy(i => i.CreatedDate).ToArray();
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
                    Labels = allDates.Select(i => Format(i, mode)).ToArray()
                };
            }

            IEnumerable<DayChartQuery> GroupDaysSignUps(IEnumerable<DayChartQuery> signUps, int days)
            {
                return signUps
                    .GroupBy(i => 
                        new Tuple<int, int, int>(
                            i.CreatedDate.Year, 
                            i.CreatedDate.Month, 
                            RoundOff(i.CreatedDate.Day, days))
                        )
                                       
                    .Select(g => 
                        new DayChartQuery 
                        { 
                            Count = g.Sum(i => i.Count), 
                            CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, g.Key.Item3) 
                        });
            }

            IEnumerable<DayChartQuery> GroupDaysActiveUsers(IEnumerable<DayChartQuery> activeUsers, int days)
            {
                return activeUsers
                    .GroupBy(i => 
                        new Tuple<int, int, int>(
                            i.CreatedDate.Year, 
                            i.CreatedDate.Month, 
                            RoundOff(i.CreatedDate.Day, days))
                        )

                    .Select(g => 
                        new DayChartQuery 
                        { 
                            Count = g.Sum(i => i.Count),
                            CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, g.Key.Item3) 
                        });
            }
        }


        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(int days = 7)
        {
            var activeUsersSql = @$"select count(*) from (
select id, name, (select job.created_at from job where job.user_id = user.id ORDER by created_at DESC limit 1) as last_job from user
) a
where datediff(UTC_TIMESTAMP(), last_job) < {days}";

            var averageProcessingTimeSql = @$"select AVG((finished_at - processed_at) / page_count) from job
where DATEDIFF(UTC_TIMESTAMP(), created_at) < {days} and status = 'completed'";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var activeUsers = await connection.ExecuteScalarAsync<int>(activeUsersSql);
                var allUsers = await connection.ExecuteScalarAsync<int>("select count(*) from user");
                var newUsers = await connection.ExecuteScalarAsync<int>($"select count(*) from user where datediff(UTC_TIMESTAMP(), created_at) < {days}");
                var verifiedUsers = await connection.ExecuteScalarAsync<int>($"select count(*) from user where verified = true");

                var averageDailyActiveUsers = Math.Round(activeUsers / (double)days, 1);
                var averageWeeklyActiveUsers = Math.Round(activeUsers / Math.Max(1, (double)days / 7), 1);
                var averageMonthlyActiveUsers = Math.Round(activeUsers / Math.Max(1, (double)days / 30), 1);

                var jobs = await connection.QueryAsync<string>($"select status from job where datediff(UTC_TIMESTAMP(), created_at) < {days}");
                var totalPages = await connection.QueryAsync<int>($"select sum(page_count) from job where datediff(UTC_TIMESTAMP(), created_at) < {days}");
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
                    totalPages,
                    totalJobs,
                    averageProcessingTimePerPage,
                    averageDailyActiveUsers,
                    averageWeeklyActiveUsers,
                    averageMonthlyActiveUsers
                });
            }
        }

        enum GroupMode
        {
            Days,
            NDays,
            Months
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
                cumulativeUsers = cumulativeUsers.Where(i => (DateTime.UtcNow - i.CreatedDate).TotalDays < days);

                var mode = GroupMode.Days;

                if (days > 120)
                {
                    cumulativeUsers = cumulativeUsers
                        .GroupBy(i => new Tuple<int, int>(i.CreatedDate.Year, i.CreatedDate.Month))
                        .Select(g => new DayChartQuery { Count = g.Last().Count, CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, 1) })
                        .OrderBy(i => i.CreatedDate)
                        .ToArray();

                    mode = GroupMode.Months;
                }
                else if (days > 90)
                {
                    cumulativeUsers = 
                        GroupDays(cumulativeUsers, 10)
                       .OrderBy(i => i.CreatedDate)
                       .ToArray();

                    mode = GroupMode.NDays;
                }
                else if (days > 30)
                {
                    cumulativeUsers =
                        GroupDays(cumulativeUsers, 5)
                       .OrderBy(i => i.CreatedDate)
                       .ToArray();

                    mode = GroupMode.NDays;
                }
                else
                {
                    cumulativeUsers = cumulativeUsers
                        .OrderBy(i => i.CreatedDate)
                        .ToArray();
                }

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
                    Labels = cumulativeUsers.Select(i => Format(i.CreatedDate, mode)).ToArray()
                };
            }

            IEnumerable<DayChartQuery> GroupDays(IEnumerable<DayChartQuery> cumulativeUsers, int days)
            {
                return cumulativeUsers
                    .GroupBy(i => 
                        new Tuple<int, int, int>(
                            i.CreatedDate.Year, i.CreatedDate.Month,
                            RoundOff(i.CreatedDate.Day, days))
                        )

                    .Select(g => 
                        new DayChartQuery 
                        { 
                            Count = g.Last().Count,
                            CreatedDate = new DateTime(g.Key.Item1, g.Key.Item2, g.Key.Item3) 
                        });
            }
        }

        int RoundOff(int number, int factor)
        {
            var result = (number / factor) * factor;
            return Math.Max(1, result);
        }

        string Format(DateTime date, GroupMode mode)
        {
            return mode == GroupMode.Months ? date.ToString("MMM yyyy") :
                   mode == GroupMode.NDays ? date.ToString("dd MMM yyyy") :
                   date.ToString("MM/dd");
        }
    }
}
