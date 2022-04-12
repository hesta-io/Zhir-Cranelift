
using Cranelift.Helpers;

using Dapper;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cranelift.Pages
{
    public class UserViewModel
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string CompanyName { get; set; }
        public int Balance { get; set; }
        public int NumberOfJobs { get; set; }
        public int NumberOfPages { get; set; }
    }

    public class Period
    {
        public string Name { get; set; }
        public int Days { get; set; }
    }

    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IDbContext _dbContext;

        public IndexModel(ILogger<IndexModel> logger, IDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        public List<UserViewModel> ActiveUsersByPageCount { get; set; }

        public static List<Period> Periods { get; } = new List<Period>
        {
            new Period { Days = 7, Name = "Past week" },
            new Period { Days = 14, Name = "Past 2 weeks" },
            new Period { Days = 30, Name = "Past month" },
            new Period { Days = 90, Name = "Past 3 months" },
            new Period { Days = 180, Name = "Past 6 months" },
            new Period { Days = 360, Name = "Past year" },
	    new Period { Days = 10000, Name = "All time" },
        };

        public Period SelectedPeriod { get; set; } = new Period { Days = 7, Name = "Past week" };

        public async Task OnGet(int days = 30)
        {
            SelectedPeriod.Days = days;
            SelectedPeriod.Name = Periods.FirstOrDefault(p => p.Days == days)?.Name ?? $"Past {days} Days";

            const int Limit = 15;
            var query = $@"select id, name, company_name, 
        (select sum(page_count) from user_transaction ut where ut.user_id = u.id and ut.confirmed = 1) as balance,
		(select sum(page_count) from job j where j.user_id = u.id and DATEDIFF(UTC_TIMESTAMP(), j.created_at) <= {SelectedPeriod.Days}) as number_of_pages,
		(select count(id) from job j2 where j2.user_id = u.id and DATEDIFF(UTC_TIMESTAMP(), j2.created_at) <= {SelectedPeriod.Days}) as number_of_jobs
from `user` u
where u.is_admin = FALSE
order by number_of_pages desc
limit {Limit}";

            using (var connection = await _dbContext.OpenOcrConnectionAsync())
            {
                var result = await connection.QueryAsync<UserViewModel>(query);
                ActiveUsersByPageCount = result.ToList();
            }
        }
    }
}
