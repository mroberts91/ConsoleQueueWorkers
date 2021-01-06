using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CbUploader
{
    public class Program
    {
        /// <summary>
        /// Copy generated Client Billing statements from the local directory
        /// directory to their appropriate file share.
        /// </summary>
        /// <param name="source">Source File Directory</param>
        /// <param name="output">Output Directory Name (eg. Dec 1 2020)</param>
        /// <param name="testrun">Indicating that output will be to test directory</param>
        /// <param name="workers">Number of worker process to transfer files</param>
        /// <returns></returns>
        static async Task Main(DirectoryInfo source, string output, bool testrun, int workers)
        {
            CreateLogger();

            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ProcessingQueue>();
                    services.AddSingleton<DeadLetterQueue>();
                    services.AddSingleton<ProgressTracker>();
                    services.AddSingleton<App>();
                    services.AddTransient<FileProcessor>();
                })
                .UseSerilog()
                .Build();

            try
            {
                await StartHost(host);

                var app = host.Services.GetRequiredService<App>();

                await app.Run(source, output, testrun, workers);

                await host.WaitForShutdownAsync().ContinueWith(task => 
                {
                    Log.Information("{msg}", "Host Shutting Down ...");
                    return task;
                });
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "{msg}", "Host Terminated Unexpectedly ...");
                await host.StopAsync();
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        static async Task StartHost(IHost host)
        {
            Log.Information("{msg}", "Starting Host ...");
            await host.StartAsync();
            Log.Information("{msg}", "Host Started ...");
        }

        static void CreateLogger()
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClientBillingCopy");
            if (!Directory.Exists(logPath)) Directory.CreateDirectory(logPath);

            Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.ControlledBy(new(LogEventLevel.Information))
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                        .MinimumLevel.Override("System", LogEventLevel.Warning)
                        .Enrich.FromLogContext()
                        .WriteTo.Console(
                            outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}",
                            theme: AnsiConsoleTheme.Literate)
                        .WriteTo.File(
                            path: Path.Combine(logPath, "log-.log"),
                            rollOnFileSizeLimit: true,
                            rollingInterval: RollingInterval.Hour,
                            shared: true,
                            flushToDiskInterval: TimeSpan.FromSeconds(10),
                            outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}")
                        .CreateLogger();
        }
    }
}