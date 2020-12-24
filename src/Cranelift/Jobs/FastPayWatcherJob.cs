using Cranelift.Helpers;

using Hangfire.Server;
using Hangfire.Console;

using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;
using Hangfire;
using Microsoft.Extensions.Configuration;

namespace Cranelift.Jobs
{
    public class FastPayWatcherJob
    {
        private readonly static Random _random = new Random();

        private readonly FastPayService _fastPayService;
        private readonly IDbContext _dbContext;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly EmailSender _emailSender;
        private readonly FastPayOptions _fastPayOptions;

        public FastPayWatcherJob(
            FastPayService fastPayService,
            IDbContext dbContext,
            IBackgroundJobClient backgroundJobClient,
            EmailSender emailSender,
            IConfiguration config)
        {
            _fastPayService = fastPayService;
            _dbContext = dbContext;
            _backgroundJobClient = backgroundJobClient;
            _emailSender = emailSender;
            _fastPayOptions = config.GetSection(Constants.FastPay).Get<FastPayOptions>();
        }

        public async Task Execute(PerformContext context)
        {
            context.WriteLine("Getting Zhir transactions...");
            using var connection = await _dbContext.OpenOcrConnectionAsync(context.CancellationToken.ShutdownToken);
            var userTransactions = await connection.GetTransactionsAsync();

            context.WriteLine("Getting Zhir users...");
            var users = await connection.GetUsersAsync();

            context.WriteLine("Getting FastPay transactions...");
            var fastPayTransactions = await _fastPayService.GetFastPayTransactionsAsync();

            var rates = new Dictionary<decimal, decimal>
            {
                { 5000, 50 },
                { 8000, 100 },
                { 30000, 500 },
                { 50000, 1000 }
            };

            foreach (var fpTransaction in fastPayTransactions)
            {
                var ut = userTransactions.FirstOrDefault(t => t.TransactionId == fpTransaction.Id.ToString() && t.PaymentMediumCode == "FASTPAY");
                if (ut != null) continue;

                var max = rates.Max(r => r.Key);

                var rate = fpTransaction.Amount <= max ?
                    rates.First(r => fpTransaction.Amount <= r.Key) :
                    rates.First(r => r.Key == max);

                // TODO: What to do if the rate sent is not equal to any of our plans?

                var userId = users.FirstOrDefault(u => Normalize(u.PhoneNo) == fpTransaction.SenderMobileNo.ToLower())?.Id;
                if (userId is null)
                {
                    await _emailSender.SendEmail("zhir.company.io@gmail.com", "Unknown transaction in FastPay", $"Can't find the owner for transaction #{fpTransaction.Id} from {fpTransaction.SenderName} ({fpTransaction.SenderMobileNo}) at {fpTransaction.Date}. Add a record for it in Cranelift to silence this alert.");

                    context.WriteLine($"Could not find any user for transaction {fpTransaction.Id}!");
                    continue;
                }

                var transaction = new UserTransaction
                {
                    Amount = fpTransaction.Amount,
                    AdminNote = "Background Job",
                    PageCount = (int)Math.Ceiling(fpTransaction.Amount / (rate.Key / rate.Value)),
                    UserId = (int)userId,
                    PaymentMediumId = UserTransaction.PaymentMediums.FastPay,
                    CreatedAt = DateTime.UtcNow,
                    TransactionId = fpTransaction.Id.ToString(),
                    TypeId = UserTransaction.Types.Recharge,
                };

                context.WriteLine($"Inserted transaction {fpTransaction.Id}");
                await connection.InsertTransactionAsync(transaction);
            }

            var delayMinutes = _random.Next(_fastPayOptions.IntervalMinMinutes, _fastPayOptions.IntervalMaxMinutes);
            _backgroundJobClient.Schedule<FastPayWatcherJob>(job => job.Execute(null), TimeSpan.FromMinutes(delayMinutes));

            context.WriteLine($"Done :) I will run again in {delayMinutes} minutes!");
        }

        private string Normalize(string phoneNo)
        {
            phoneNo = phoneNo.Replace(" ", "");

            if (string.IsNullOrEmpty(phoneNo))
                return phoneNo;

            if (phoneNo.StartsWith("+964"))
                return phoneNo;

            if (phoneNo.StartsWith("964"))
                return "+" + phoneNo;

            if (phoneNo.StartsWith("0"))
                return "+964" + phoneNo.Substring(1);

            return "+964" + phoneNo;
        }
    }
}
