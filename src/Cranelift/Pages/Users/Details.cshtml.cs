using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cranelift.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cranelift.Pages.Users
{
    [Microsoft.AspNetCore.Components.Route("/users")]
    public class DetailsModel : PageModel
    {
        private readonly IDbContext _dbContext;

        public DetailsModel(IDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public User Data { get; set; }

        public List<Queries.TranactionViewModel> Transactions { get; set; }

        public async Task OnGet(int id)
        {
            using var connection = await _dbContext.OpenOcrConnectionAsync();
            Data = await connection.GetUserAsync(id);
            var transactions = await connection.GetTransactions(id);
            Transactions = transactions.ToList();
        }
    }
}
