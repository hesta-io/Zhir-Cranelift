﻿using Cranelift.Helpers;

using Hangfire.Console;
using Hangfire.Server;

using Microsoft.Extensions.Configuration;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace Cranelift.Jobs
{
    public class BillingOptions
    {
        public bool EnforceBalance { get; set; }
        public int FreeMonthlyPages { get; set; }
    }

    public class MonthlyGiftJob
    {
        private readonly IDbContext _dbContext;
        private BillingOptions _billingOptions;

        public MonthlyGiftJob(
            IConfiguration configuration,
            IDbContext dbContext)
        {
            _billingOptions = configuration.GetSection(Constants.Billing).Get<BillingOptions>();
            _dbContext = dbContext;
        }

        public async Task Execute(PerformContext performContext)
        {
            using var connection = await _dbContext.OpenOcrConnectionAsync(performContext.CancellationToken.ShutdownToken);
            using var transaction = await connection.BeginTransactionAsync(performContext.CancellationToken.ShutdownToken);

            var users = await connection.GetUsersAsync();
            var eligibleUsers = users.Where(u => u.Balance < _billingOptions.FreeMonthlyPages).ToArray();

            performContext.WriteLine($"There are {eligibleUsers.Length:N0} eligible users.");

            var total = 0;

            foreach (var user in eligibleUsers)
            {
                var ut = new UserTransaction
                {
                    UserId = user.Id,
                    Amount = 0,
                    UserNote = "دیاری :)",
                    AdminNote = "Monthly free pages",
                    PaymentMediumId = UserTransaction.PaymentMediums.Zhir,
                    TypeId = UserTransaction.Types.Recharge, // Should we make a dedicated transaction type for this?
                    CreatedAt = DateTime.UtcNow,
                    PageCount = _billingOptions.FreeMonthlyPages - (int)user.Balance,
                };

                await connection.InsertTransactionAsync(ut);
                total += ut.PageCount;
            }

            performContext.WriteLine($"Gave away {total:N0} pages :)");
            await transaction.CommitAsync();
        }
    }
}