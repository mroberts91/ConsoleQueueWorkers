using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace CbUploader
{
    public class ProgressTracker
    {
        private readonly ILogger<ProgressTracker> _logger;

        public ProgressTracker(ILogger<ProgressTracker> logger)
        {
            _logger = logger;
        }

        public int FilesToProcess { get; private set; } = 0;
        public int FilesProcessed { get; private set; } = 0;
        public int FilesAttempted { get; private set; } = 0;

        public void SetFilesToProcessCount(int count) => FilesToProcess = count;

        public void IncrementAttemptedCount() => ++FilesAttempted;

        public void IncrementProcessedCount()
        {
            ++FilesProcessed;

            if (FilesProcessed != 0 && FilesProcessed % 500 == 0)
                _logger.LogInformation("File Copy In Progress ... {cnt} files attempted, {pro} files copied", FilesAttempted, FilesProcessed);
        }
    }
}
