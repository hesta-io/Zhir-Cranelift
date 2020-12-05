using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cranelift.Helpers;

using Dapper;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cranelift.Pages
{
    public class JobViewModel
    {
        public long UserId { get; set; }
        public string UserName { get; set; }
        public string JobName { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime ProcessedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
        public string FailingReason { get; set; }
        public int PageCount { get; set; }
        public string Id { get; set; }

        public double SecondsInQueue => (ProcessedAt - CreatedAt).TotalSeconds;
        public double SecondsInProcessing => (FinishedAt - ProcessedAt).TotalSeconds;

        public string Pdf => $"https://test.zhir.io/assets/done/{UserId}/{Id}/result.pdf";
        public string Word => $"https://test.zhir.io/assets/done/{UserId}/{Id}/result.docx";
        public string Text => $"https://test.zhir.io/assets/done/{UserId}/{Id}/result.txt";
    }

    [Authorize]
    public class JobsModel : PageModel
    {
        private readonly IDbContext _dbContext;

        public JobsModel(IDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public List<JobViewModel> Jobs { get; private set; }

        public async Task OnGet()
        {
            using var connection = await _dbContext.OpenOcrConnectionAsync();

            var sql = @"select j.id, j.name as job_name, u.name as user_name, u.id as user_id, j.queued_at, j.processed_at, j.finished_at, j.created_at, j.status, j.failing_reason, j.page_count from job j
left join `user` u on u.id = j.user_id 
order by j.created_at DESC";

            var jobs = await connection.QueryAsync<JobViewModel>(sql);
            Jobs = jobs.ToList();
        }
    }
}
