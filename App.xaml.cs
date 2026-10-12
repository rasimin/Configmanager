using System.Windows;
using ConfigManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConfigManager;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddWpfBlazorWebView();
#if DEBUG
        serviceCollection.AddBlazorWebViewDeveloperTools();
#endif

        serviceCollection.AddSingleton<IConfigParserService, ConfigParserService>();
        serviceCollection.AddSingleton<IConfigScanService, ConfigScanService>();
        serviceCollection.AddSingleton<IFolderPickerService, FolderPickerService>();

        Services = serviceCollection.BuildServiceProvider();
        Resources.Add("services", Services);
    }
}
