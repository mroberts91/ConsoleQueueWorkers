using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CbUploader
{
    public class App
    {
        private readonly IHostApplicationLifetime _host;
        private readonly IServiceProvider _services;
        private readonly ILogger _logger;
        private readonly ProgressTracker _progress;
        private readonly ProcessingQueue _processingQueue;
        private readonly DeadLetterQueue _deadLetterQueue;
        private readonly CancellationTokenSource _workerTokenSource;

        public App(IHostApplicationLifetime host, ILoggerFactory loggerFactory, IServiceProvider services,
                   ProgressTracker progress, ProcessingQueue processingQueue, DeadLetterQueue deadLetterQueue)
        {
            _host = host;
            _services = services;
            _progress = progress;
            _logger = loggerFactory.CreateLogger(GetType().FullName);
            _processingQueue = processingQueue;
            _deadLetterQueue = deadLetterQueue;
            _workerTokenSource = new();
        }

        public async Task Run(DirectoryInfo sourceDirectory, string outputDirectoryName, bool testRun, int workers)
        {

            _logger.LogInformation("Validating Source Directory {dir}", sourceDirectory.FullName);
            _logger.LogInformation("Setting output paths to {dirname}", outputDirectoryName);
            
            Stopwatch stopwatch = new();
            stopwatch.Start();

            await Task.WhenAll(
                LoadBranches(sourceDirectory, outputDirectoryName, testRun),
                LoadNcAgencies(sourceDirectory, outputDirectoryName, testRun),
                LoadNmAgencies(sourceDirectory, outputDirectoryName, testRun),
                LoadSrcn(sourceDirectory, outputDirectoryName, testRun),
                LoadMbdot(sourceDirectory, outputDirectoryName, testRun)
            );

            _progress.SetFilesToProcessCount(await _processingQueue.CurrentCount());
            _logger.LogInformation("Queue Loaded: {queue}", _processingQueue.ToString());

            
            var workerPool = Enumerable.Range(0, workers).Select(_ => _services.GetRequiredService<FileProcessor>());
            var workerTasks = workerPool.Select(w => w.StartAsync(_workerTokenSource.Token));
            await Task.WhenAll(workerTasks);


            // HACK Need to figure out a better way to determine the end of processing
            bool running = true;
            while (running)
            {
                await Task.Delay(3000);
                running = await _processingQueue.CurrentCount() > 0;
            }

            stopwatch.Stop();
            _logger.LogInformation("Stats:\n\tElapsed Time:\t{time} ms\n\tFiles Attempted:\t{at} files\n\tFiles Copied:\t{cp} files\n\tWorker Count:\t{w}",
                stopwatch.ElapsedMilliseconds,
                _progress.FilesAttempted,
                _progress.FilesProcessed,
                workers);

            var stopTasks = workerPool.Select(w => w.StopAsync(_workerTokenSource.Token));
            await Task.WhenAll(stopTasks);

            await Finalize();
        }

        #region Load Methods
        private async Task LoadBranches(DirectoryInfo sourceDirectory, string outputDirectoryName, bool testRun)
        {
            DirectoryInfo branchDirectory = new (Path.Combine(sourceDirectory.FullName, "NC Branches"));
            if (!branchDirectory.Exists)
            {
                _logger.LogError("Unable to locate NC Branches directory at path {path}", branchDirectory);
                throw new ApplicationException($"Unable to locate NC Branches directory at path { branchDirectory }");
            }

            var fileTasks = branchDirectory.GetDirectories("*", SearchOption.TopDirectoryOnly)
                .Select(d => d.GetFiles())
                .SelectMany(fi => fi.Select(f => Task.Run(() => ConstructOutputFile(f, outputDirectoryName, StatementType.NcBranch, testRun))));

            var topLevelFileTasks = branchDirectory.GetFiles()
                .Select(f => Task.Run(() => ConstructOutputFile(f, outputDirectoryName, StatementType.NcBranch, testRun)));
            
            var files = await Task.WhenAll(fileTasks.Concat(topLevelFileTasks));
            await _processingQueue.Add(files);
        }

        private async Task LoadNcAgencies(DirectoryInfo sourceDirectory, string outputDirectoryName, bool testRun)
        {
            DirectoryInfo branchDirectory = new(Path.Combine(sourceDirectory.FullName, "NC Agencies"));
            if (!branchDirectory.Exists)
            {
                _logger.LogError("Unable to locate NC Agencies directory at path {path}", branchDirectory);
                throw new ApplicationException($"Unable to locate NC Agencies directory at path { branchDirectory }");
            }

            var fileTasks = branchDirectory.GetDirectories("*", SearchOption.TopDirectoryOnly)
                .Select(d => d.GetFiles())
                .SelectMany(fi => fi.Select(f => Task.Run(() => ConstructOutputFile(f, outputDirectoryName, StatementType.NcAgency, testRun))));

            var files = await Task.WhenAll(fileTasks);
            await _processingQueue.Add(files);
        }

        private async Task LoadNmAgencies(DirectoryInfo sourceDirectory, string outputDirectoryName, bool testRun)
        {
            DirectoryInfo branchDirectory = new(Path.Combine(sourceDirectory.FullName, "NM Agencies"));
            if (!branchDirectory.Exists)
            {
                _logger.LogError("Unable to locate NM Agencies directory at path {path}", branchDirectory);
                throw new ApplicationException($"Unable to locate NM Agencies directory at path { branchDirectory }");
            }

            var fileTasks = branchDirectory.GetDirectories("*", SearchOption.AllDirectories)
                .Select(d => d.GetFiles())
                .SelectMany(fi => fi.Select(f => Task.Run(() => ConstructOutputFile(f, outputDirectoryName, StatementType.NmAgency, testRun))));

            var files = await Task.WhenAll(fileTasks);
            await _processingQueue.Add(files);
        }

        private async Task LoadSrcn(DirectoryInfo sourceDirectory, string outputDirectoryName, bool testRun)
        {
            DirectoryInfo branchDirectory = new(Path.Combine(sourceDirectory.FullName, "SRCN"));
            if (!branchDirectory.Exists)
            {
                _logger.LogError("Unable to locate SRCN directory at path {path}", branchDirectory);
                throw new ApplicationException($"Unable to locate SRCN directory at path { branchDirectory }");
            }

            var fileTasks = branchDirectory.GetFiles()
                .Select(f => Task.Run(() => ConstructOutputFile(f, outputDirectoryName, StatementType.Srcn, testRun)));

            var files = await Task.WhenAll(fileTasks);
            await _processingQueue.Add(files);
        }

        private async Task LoadMbdot(DirectoryInfo sourceDirectory, string outputDirectoryName, bool testRun)
        {
            DirectoryInfo branchDirectory = new(Path.Combine(sourceDirectory.FullName, "MBDOT"));
            if (!branchDirectory.Exists)
            {
                _logger.LogError("Unable to locate MBDOT directory at path {path}", branchDirectory);
                throw new ApplicationException($"Unable to locate MBDOT directory at path { branchDirectory }");
            }

            var fileTasks = branchDirectory.GetDirectories("*", SearchOption.TopDirectoryOnly)
                .Select(d => d.GetFiles())
                .SelectMany(fi => fi.Select(f => Task.Run(() => ConstructOutputFile(f, outputDirectoryName, StatementType.Mbdot, testRun))));

            var topLevelFileTasks = branchDirectory.GetFiles()
                .Select(f => Task.Run(() => ConstructOutputFile(f, outputDirectoryName, StatementType.Mbdot, testRun)));

            var files = await Task.WhenAll(fileTasks.Concat(topLevelFileTasks));
            await _processingQueue.Add(files);
        }
        #endregion Load Methods

        private ClientBillingFile ConstructOutputFile(FileInfo file, string outputDirectoryName, StatementType statementType, bool testRun)
        {
            var filePath = (statementType switch
            {
                StatementType.NcBranch => Regex.Match(file.FullName, @"(?<=NC Branches\\).*$"),
                StatementType.NcAgency => Regex.Match(file.FullName, @"(?<=NC Agencies\\).*$"),
                StatementType.NmAgency => Regex.Match(file.FullName, @"(?<=NM Agencies\\).*$"),
                StatementType.Srcn => Regex.Match(file.FullName, @"(?<=SRCN\\).*$"),
                StatementType.Mbdot => Regex.Match(file.FullName, @"(?<=MBDOT\\).*$"),
                StatementType.DirectOps => Regex.Match(file.FullName, @"(?<=NC Branches\\).*$"),
            }).Value;

            FileInfo outputFile = (statementType, testRun) switch
            {
                (StatementType.NcBranch,    true) => new(Path.Combine(Constants.BranchesPathTest, outputDirectoryName, filePath)),
                (StatementType.NcAgency,    true) => new(Path.Combine(Constants.NcAgencyPathTest, outputDirectoryName, filePath)),
                (StatementType.NmAgency,    true) => new(Path.Combine(Constants.NmAgencyPathTest, outputDirectoryName, filePath)),
                (StatementType.Srcn,        true) => new(Path.Combine(Constants.SrcnPathTest, outputDirectoryName, filePath)),
                (StatementType.Mbdot,       true) => new(Path.Combine(Constants.MbdotPathTest, outputDirectoryName, filePath)),
                (StatementType.DirectOps,   true) => new(Path.Combine(Constants.DirectOpsPathTest, outputDirectoryName, filePath)),

                (StatementType.NcBranch,    false) => new(Path.Combine(Constants.BranchesPath, outputDirectoryName, filePath)),
                (StatementType.NcAgency,    false) => new(Path.Combine(Constants.NcAgencyPath, outputDirectoryName, filePath)),
                (StatementType.NmAgency,    false) => new(Path.Combine(Constants.NmAgencyPath, outputDirectoryName, filePath)),
                (StatementType.Srcn,        false) => new(Path.Combine(Constants.SrcnPath, outputDirectoryName, filePath)),
                (StatementType.Mbdot,       false) => new(Path.Combine(Constants.MbdotPath, outputDirectoryName, filePath)),
                (StatementType.DirectOps,   false) => new(Path.Combine(Constants.DirectOpsPath, outputDirectoryName, filePath)),
            };

            return new(file, outputFile, 0);
        }

        private async Task Finalize()
        {
            if (!await _deadLetterQueue.IsEmpty())
                _logger.LogInformation("Unable to process {cnt} files. Verbose logs have been written", await _deadLetterQueue.CurrentCount());
            _logger.LogInformation("{msg}", "App finalizing ...");
            _host.StopApplication();
        }
    }
}
