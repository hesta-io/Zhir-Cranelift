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
    public class JobDto
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
        public string Lang { get; set; }
        public int Rate { get; set; }

        public double SecondsInQueue => (ProcessedAt - CreatedAt).TotalSeconds;
        public double SecondsInProcessing => (FinishedAt - ProcessedAt).TotalSeconds;

        public string Pdf => $"https://zhir.io/assets/done/{UserId}/{Id}/result.pdf";
        public string Word => $"https://zhir.io/assets/done/{UserId}/{Id}/result.docx";
        public string Text => $"https://zhir.io/assets/done/{UserId}/{Id}/result.txt";
    }

    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class JobsController : Controller
    {
        private readonly IDbContext _dbContext;

        public JobsController(IDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IEnumerable<JobDto>> GetAll()
        {
            using var connection = await _dbContext.OpenOcrConnectionAsync();

            var sql = @"select j.id, j.name as job_name, j.lang as Lang, u.name as user_name,j.rate, u.id as user_id, j.queued_at, j.processed_at, j.finished_at, j.created_at, j.status, j.failing_reason, j.page_count from job j
left join `user` u on u.id = j.user_id 
order by j.created_at DESC";

            var jobs = await connection.QueryAsync<JobDto>(sql);

            return jobs;
        }
    }
}
