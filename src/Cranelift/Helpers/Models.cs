
using System;

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

    public class Job
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public int UserId { get; set; }
        public int PageCount { get; set; }
        public string Status { get; set; }
        public int PricePerPage { get; set; } // This should be decimal?
        public DateTime ChangedToQueue { get; set; } // QueuedAt ?
        public DateTime ChangedToProcessing { get; set; } // ProcessedAt ?
        public DateTime ChangedToCompleted { get; set; } // CompletedAt ?
        public DateTime ChangedToFailed { get; set; } // Maybe we only need changed to completed?
        public string FailingReason { get; set; }
        public bool? Deleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
    }
}
