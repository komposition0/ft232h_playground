using FT232H.ControlStudio.UI;
using FT232H.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FT232H.ControlStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Core services
                services.AddSingleton<InMemoryTransactionLogger>();
                services.AddSingleton<ITransactionLogger>(sp => sp.GetRequiredService<InMemoryTransactionLogger>());
                services.AddSingleton<CommunicationCounters>();
                services.AddSingleton<StudioContext>();

                // Automation
                services.AddSingleton<FT232H.Automation.TestSequenceRunner>();

                // Logging routed to the in-app log viewer
                services.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddProvider(new StudioLogProvider());
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // WinForms
                services.AddSingleton<MainForm>();
            })
            .Build();

        Application.Run(host.Services.GetRequiredService<MainForm>());
    }
}

/// <summary>
/// Routes Microsoft.Extensions.Logging output to the studio log pane via the
/// transaction logger, so library and app logs appear in one viewer.
/// </summary>
file sealed class StudioLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StudioLogger(categoryName);
    public void Dispose() { }

    private sealed class StudioLogger : ILogger
    {
        private readonly string _category;
        public StudioLogger(string category) => _category = category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var severity = logLevel switch
            {
                LogLevel.Trace => DiagnosticSeverity.Trace,
                LogLevel.Debug => DiagnosticSeverity.Trace,
                LogLevel.Information => DiagnosticSeverity.Info,
                LogLevel.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Error,
            };
            StudioLogSink.Instance.Append(severity, _category, formatter(state, exception),
                exception?.ToString());
        }
    }
}