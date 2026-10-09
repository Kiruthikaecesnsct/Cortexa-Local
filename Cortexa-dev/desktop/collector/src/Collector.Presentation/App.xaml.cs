using System.Windows;
using System.Windows.Threading;
using Collector.Application.Auth;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Presentation.Hosting;
using Collector.Presentation.Themes;
using Collector.Presentation.ViewModels;
using Collector.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly UnhandledErrorReporter _reporter;
    private IHost? _host;
    private ThemeService? _theme;

    public App() => _reporter = new UnhandledErrorReporter(() => _host?.Services.GetService<ILogger<App>>());

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        HookUnhandledHandlers();
        _theme = new ThemeService(this);
        _theme.Start();
        try
        {
            await StartHostAsync();
        }
        catch (Exception ex)
        {
            _reporter.Report(ex, "startup");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _theme?.Stop();
        StopHost();
        base.OnExit(e);
    }

    private async Task StartHostAsync()
    {
        _host = CollectorHost.Build();
        await _host.StartAsync();
        var services = _host.Services;
        await services.GetRequiredService<ILocalCacheInitializer>().InitializeAsync(CancellationToken.None);
        await services.GetRequiredService<StaleRemoteSecretPurge>().RunAsync(CancellationToken.None);
        await services.GetRequiredService<ISignInService>().RestoreAsync(CancellationToken.None);
        var window = services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.DataContext = services.GetRequiredService<ShellViewModel>();
        services.GetRequiredService<ShellViewModel>().Start();
        window.Show();
    }

    private void StopHost()
    {
        if (_host is null)
        {
            return;
        }

        Task.Run(() => _host.StopAsync(new CancellationTokenSource(StopTimeout).Token)).Wait(StopTimeout);
        _host.Dispose();
        _host = null;
    }

    private void HookUnhandledHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _reporter.Report(args.ExceptionObject as Exception, "app domain");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _reporter.Report(args.Exception, "task scheduler");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _reporter.Report(e.Exception, "dispatcher");
        e.Handled = true;
    }
}
