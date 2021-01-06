using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CbUploader
{
    public class FileProcessor : BackgroundService
    {
        private readonly ILogger<FileProcessor> _logger;
        private readonly IServiceProvider _services;

        public FileProcessor(IServiceProvider services, ILogger<FileProcessor> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await CopyFile(stoppingToken);
            }
        }

        private async Task CopyFile(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _services.CreateScope();
                var processingQueue = scope.ServiceProvider.GetRequiredService<ProcessingQueue>();
                var deadLetterQueue = scope.ServiceProvider.GetRequiredService<DeadLetterQueue>();
                var tracker = scope.ServiceProvider.GetRequiredService<ProgressTracker>();
                var file = await processingQueue.GetNext();

                if (file != null)
                {
                    try
                    {
                        tracker.IncrementAttemptedCount();

                        if (!Directory.Exists(file.OutputFile.DirectoryName) && file.OutputFile.DirectoryName is { })
                            Directory.CreateDirectory(file.OutputFile.DirectoryName);

                        File.Copy(file.File.FullName, file.OutputFile.FullName, true);
                        
                        tracker.IncrementProcessedCount();
                    }   
                    catch
                    {
                        var atempts = file.Attempts + 1;
                        var newFile = file with { Attempts = atempts };

                        if (newFile.Attempts < 3)
                            await processingQueue.Add(newFile);
                        else
                            await deadLetterQueue.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to send request due to exception: {message}", ex.Message);
            }
        }
    }
}
