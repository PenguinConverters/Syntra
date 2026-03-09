using Microsoft.Extensions.Logging;

namespace PenguinConverters.Syntra.Host.Console;

/// <summary>
/// Entry point for the Syntra console host application.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main entry point. Parses command-line arguments and runs the synchronization worker.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments:
    /// <list type="bullet">
    /// <item><c>--configuration={file}</c> - Path to the YAML configuration file (required).</item>
    /// <item><c>--schema</c> - Enables SchemaDesigner mode for schema inference.</item>
    /// </list>
    /// </param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    public static int Main(string[] args)
    {
        string? configurationPath = null;
        bool schemaMode = false;

        foreach (string arg in args)
        {
            if (arg.StartsWith("--configuration=", StringComparison.OrdinalIgnoreCase))
            {
                configurationPath = arg["--configuration=".Length..];
            }
            else if (arg.Equals("--schema", StringComparison.OrdinalIgnoreCase))
            {
                schemaMode = true;
            }
        }

        if (string.IsNullOrWhiteSpace(configurationPath))
        {
            System.Console.Error.WriteLine("Usage: PenguinConverters.Syntra.Host.Console --configuration={file} [--schema]");
            System.Console.Error.WriteLine();
            System.Console.Error.WriteLine("Arguments:");
            System.Console.Error.WriteLine("  --configuration={file}  Path to the YAML configuration file.");
            System.Console.Error.WriteLine("  --schema                Enable SchemaDesigner mode for schema inference.");
            return 1;
        }

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        ILogger logger = loggerFactory.CreateLogger(nameof(Worker));

        try
        {
            Worker worker = new Worker(configurationPath, schemaMode, logger);
            worker.Run();
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unhandled exception during synchronization");
            return 1;
        }
    }
}
