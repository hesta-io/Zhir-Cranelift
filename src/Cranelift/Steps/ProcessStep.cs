using Hangfire;
using Hangfire.Console;
using Hangfire.Server;

using System.Threading.Tasks;
namespace Cranelift.Steps
{
    public class ProcessStep
    {
        [JobDisplayName("Processing job {0}")]
        public Task Execute(string jobId, PerformContext context)
        {
            context.WriteLine($"Processing {jobId}");
            return Task.CompletedTask;
            // Step 1: Make sure the job is not processed
            // Step 2: Download images

            // LOOP A:
            // Step 3: Pre-process images
            // Step 4: Process images
            // Step 5: Save results
            // Step 6: Go back to LOOP A until all images are processed

            // Step 7: Update job status
        }
    }
}
