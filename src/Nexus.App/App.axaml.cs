using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Nexus.App.ViewModels;
using Nexus.App.Views;

namespace Nexus.App;

public partial class App : Application
{
    private AppHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            _host = new AppHost();
            vm.SetHost(_host);

            desktop.MainWindow = new MainWindow { DataContext = vm };

            desktop.ShutdownRequested += async (_, _) =>
            {
                if (_host is not null)
                    await _host.DisposeAsync();
            };

            _ = _host.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
