using Cranelift.Helpers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cranelift.Common.Models;

namespace Cranelift.Pages.Users
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly IDbContext _dbContext;

        public IndexModel(IDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public List<User> Users { get; set; }

        public async Task OnGet()
        {
            using var connection = await _dbContext.OpenOcrConnectionAsync();
            var users = await connection.GetUsersAsync();
            Users = users.ToList();
        }
    }
}
