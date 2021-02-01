
using System;
using System.Linq;

namespace Cranelift.Helpers
{
    public static class ModelConstants
    {
        public const string Pending = "pending";
        public const string Queued = "queued";
        public const string Processing = "processing";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public class UserTransaction
    {
        public class Types
        {
            public const int Recharge = 1;
            public const int OcrJob = 2;
            public const int BalanceTransfer = 2;
        }

        public class PaymentMediums
        {
            public const int Zhir = 1;
            public const int FastPay = 2;
            public const int AsiaHawala = 3;
            public const int ZainCash = 4;
            public const int ZhirBalance = 5;
        }

        public int Id { get; set; }
        public int UserId { get; set; }
        public int TypeId { get; set; }
        public int PaymentMediumId { get; set; }
        public int PageCount { get; set; }
        public decimal? Amount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string TransactionId { get; set; }
        public int CreatedBy { get; set; }
        public string UserNote { get; set; }
        public bool? Confirmed { get; set; }
        public string AdminNote { get; set; }
    }

    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string CompanyName { get; set; }
        public string Email { get; set; }
        public string PhoneNo { get; set; }
        public bool? Deleted { get; set; }
        public decimal Balance { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool? Verified { get; set; }
        public bool? IsAdmin { get; set; }

        public bool? CanUseAPI { get; set; }
        public string APIKey { get; set; }
        public int? MonthlyRecharge { get; set; }

        public int CountJobs { get; set; }
        public int CountPages { get; set; }
        public decimal MoneySpent { get; set; }
    }

    public class Job
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Lang { get; set; }
        public int UserId { get; set; }
        public int PageCount { get; set; }
        public int PaidPageCount { get; set; }
        public string Status { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime ProcessedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public string FailingReason { get; set; }
        public string UserFailingReason { get; set; }
        public bool? Deleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }

        public string[] GetLanguages()
        {
            if (string.IsNullOrWhiteSpace(Lang))
                return new string[0];

            return Lang.Split(",").Select(l => l.Trim()).ToArray();
        }

        public bool HasFinished()
        {
            return Status == ModelConstants.Completed || Status == ModelConstants.Failed; 
        }
    }

    public class Page
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int UserId { get; set; }
        public string JobId { get; set; }
        public DateTime StartedProcessingAt { get; set; }
        public DateTime FinishedProcessingAt { get; set; }
        public bool Succeeded { get; set; }
        public bool Processed { get; set; }
        public string Result { get; set; }
        public bool Deleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsFree { get; set; }
        public int CreatedBy { get; set; }
        public string FullPath { get; set; }
        public string HocrResult { get; set; }

        public bool PredictSizes { get; set; }
        public byte[] PdfResult { get; set; }
    }
}
