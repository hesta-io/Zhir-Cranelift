
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
        public int NumberOfJobs { get; set; }
        public int NumberOfPages { get; set; }
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

        public async Task OnGet()
        {
            const int NumberOfDays = 30;
            const int Limit = 10;
            var query = $@"select id, name, company_name, 
		(select sum(page_count) from job j where j.user_id = u.id and DATEDIFF(UTC_TIMESTAMP(), j.created_at) <= {NumberOfDays}) as number_of_pages,
		(select count(id) from job j2 where j2.user_id = u.id and DATEDIFF(UTC_TIMESTAMP(), j2.created_at) <= {NumberOfDays}) as number_of_jobs
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
