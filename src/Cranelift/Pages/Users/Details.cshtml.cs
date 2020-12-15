using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cranelift.Helpers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cranelift.Pages.Users
{
    [Authorize]
    [Microsoft.AspNetCore.Components.Route("/users")]
    public class DetailsModel : PageModel
    {
        private readonly IDbContext _dbContext;

        public DetailsModel(IDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public bool Success { get; set; }

        public string Message { get; set; }

        [BindProperty]
        public int Id { get; set; }

        [BindProperty]
        public int PaymentMedium { get; set; }

        [BindProperty]
        public string TransactionId { get; set; }

        [BindProperty]
        public string UserNote { get; set; }

        [BindProperty]
        public string AdminNote { get; set; }

        [BindProperty]
        public int PageCount { get; set; }

        [BindProperty]
        public decimal Amount { get; set; }

        [BindProperty]
        public bool Verified { get; set; }

        public User Data { get; set; }

        public List<Queries.TranactionViewModel> Transactions { get; set; }

        public async Task OnGet(int id)
        {
            Id = id;

            await Load(id);
        }

        private async Task Load(int id)
        {
            using var connection = await _dbContext.OpenOcrConnectionAsync();
            Data = await connection.GetUserAsync(id);
            Verified = Data.Verified == true;
            var transactions = await connection.GetTransactionsAsync(id);
            Transactions = transactions.ToList();
        }

        public async Task OnPostUser()
        {
            if (Id > 0)
            {
                using var connection = await _dbContext.OpenOcrConnectionAsync();
                var user = await connection.GetUserAsync(Id);
                user.Verified = Verified;
                await connection.UpdateUserAsync(user);
            }

            await Load(Id);
        }

        public async Task OnPostRecharge()
        {
            if (PaymentMedium < UserTransaction.PaymentMediums.Zhir ||
                PaymentMedium > UserTransaction.PaymentMediums.ZhirBalance)
            {
                Message = "Invalid payment medium.";
                Success = false;
            }
            else
            {
                var transaction = new UserTransaction
                {
                    Amount = Amount,
                    CreatedAt = DateTime.UtcNow,
                    PaymentMediumId = PaymentMedium,
                    TypeId = UserTransaction.Types.Recharge,
                    TransactionId = TransactionId,
                    PageCount = PageCount,
                    UserId = Id,
                    UserNote = UserNote,
                    AdminNote = AdminNote
                };

                var connection = await _dbContext.OpenOcrConnectionAsync();
                try
                {
                    await connection.InsertTransactionAsync(transaction);
                    Message = "Account was recharged successfuly.";
                    Success = true;
                }
                catch (Exception ex)
                {
                    Message = ex.Message;
                    Success = false;
                }
            }

            await Load(Id);
        }
    }
}
