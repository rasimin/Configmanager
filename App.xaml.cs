using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ConfigManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConfigManager;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private static ILoggerService? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddWpfBlazorWebView();
#if DEBUG
        serviceCollection.AddBlazorWebViewDeveloperTools();
#endif

        var logger = new LoggerService();
        _logger = logger;

        serviceCollection.AddSingleton<ILoggerService>(logger);
        serviceCollection.AddSingleton<IConfigParserService, ConfigParserService>();
        serviceCollection.AddSingleton<IConfigScanService, ConfigScanService>();
        serviceCollection.AddSingleton<IFolderPickerService, FolderPickerService>();

        Services = serviceCollection.BuildServiceProvider();
        Resources.Add("services", Services);

        // Register Global Exception Handlers
        SetupExceptionHandling();

        _logger.LogInfo("ConfigManager Application Started Successfully.");
    }

    private void SetupExceptionHandling()
    {
        // 1. WPF Dispatcher (UI Thread) Unhandled Exceptions
        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                _logger?.LogError($"Dispatcher Unhandled Exception: {args.Exception.Message}", args.Exception);

                var logPath = _logger?.CurrentLogFilePath ?? "logs/error.log";
                MessageBox.Show(
                    $"Terjadi kesalahan pada sistem:\n{args.Exception.Message}\n\nDetail lengkap telah dicatat di file log:\n{logPath}",
                    "ConfigManager - Terjadi Kesalahan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // Prevent immediate crash / auto-close if possible
                args.Handled = true;
            }
            catch
            {
                // Fallback if message box itself fails
            }
        };

        // 2. AppDomain Unhandled Exceptions (Non-UI / Background threads)
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            _logger?.LogError($"AppDomain Unhandled Exception: {ex?.Message ?? "Unknown Error"}", ex);
        };

        // 3. TaskScheduler Unobserved Task Exceptions (Async / Task background)
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            _logger?.LogError($"TaskScheduler Unobserved Exception: {args.Exception.Message}", args.Exception);
            args.SetObserved();
        };
    }
}
