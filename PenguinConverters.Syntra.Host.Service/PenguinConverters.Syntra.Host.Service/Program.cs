using Microsoft.Extensions.Hosting;

namespace PenguinConverters.Syntra.Host.Service;

/// <summary>
/// Entry point for the Syntra background service host.
/// Supports running as a Windows Service or a Linux systemd daemon.
/// </summary>
public static class Program
{
    /// <summary>
    /// Configures and starts the generic host with Windows Service and systemd support.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the host builder.</param>
    public static void Main(string[] args)
    {
        IHostBuilder hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .UseSystemd()
            .ConfigureServices((context, services) =>
            {
                services.AddHostedService<Worker>();
            });

        IHost host = hostBuilder.Build();
        host.Run();
    }
}
