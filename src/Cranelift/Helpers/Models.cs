
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
        public decimal? PricePerPage { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime ProcessedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public string FailingReason { get; set; }
        public bool? Deleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }

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
        public string FormatedResult { get; set; }
        public bool Deleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public string FullPath { get; set; }
    }
}
